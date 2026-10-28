using SincTrello.Core;
using Xunit;

namespace SincTrello.Core.Tests;

public class SyncEngineTests
{
    private static SyncPlan Plan(string target, string? current = null, bool available = true,
        string[]? occupied = null, string[]? known = null) => new SyncEngine().BuildPlan(
        new([new("id", "Card", "list", target)]),
        new(available, current is null ? [] : [new("id", current)], occupied ?? [], known ?? []));

    [Fact] public void CorrectFolderHasNoOperations()
    { var plan = Plan(@"Pago\Card", @"Pago\Card"); Assert.Empty(plan.Operations); Assert.Equal(1, plan.NoChanges); }
    [Theory]
    [InlineData(@"Entrada\Card", SyncOperationType.MoveDirectory)]
    [InlineData(@"Pago\Antigo", SyncOperationType.RenameDirectory)]
    [InlineData(@"Entrada\Antigo", SyncOperationType.MoveAndRename)]
    public void DetectsDelta(string current, SyncOperationType expected)
    { Assert.Equal(expected, Assert.Single(Plan(@"Pago\Card", current).Operations).Type); }
    [Fact] public void NewCardCreatesDirectory() => Assert.Equal(SyncOperationType.CreateDirectory, Assert.Single(Plan(@"Pago\Card").Operations).Type);
    [Fact] public void DisconnectedStorageBlocksAllOperations()
    { var plan = Plan(@"Pago\Card", available: false); Assert.False(plan.CanExecute); Assert.Empty(plan.Operations); }
    [Fact] public void OccupiedDestinationBlocksCreation() => Assert.False(Plan(@"Pago\Card", occupied: [@"Pago\Card"]).CanExecute);
    [Fact] public void KnownMissingFolderIsNotDuplicated() => Assert.False(Plan(@"Pago\Card", known: ["id"]).CanExecute);
    [Fact] public void DuplicateIdentityBlocksPlan()
    {
        var plan = new SyncEngine().BuildPlan(new([new("id", "Card", "l", @"Pago\Card")]),
            new(true, [new("id", @"A\Card"), new("id", @"B\Card")], [], []));
        Assert.False(plan.CanExecute); Assert.Empty(plan.Operations);
    }
    [Fact] public void MissingCardPreservesFolderAndWarns()
    { var plan = new SyncEngine().BuildPlan(new([]), new(true, [new("id", @"Pago\Card")], [], [])); Assert.Empty(plan.Operations); Assert.Single(plan.Warnings); }
    [Fact] public void OnlyDivergentCardChanges()
    {
        var desired = Enumerable.Range(0, 51).Select(i => new DesiredCardState($"{i}", $"Card{i}", "l", $@"Pago\Card{i}")).ToArray();
        var actual = desired.Select((c, i) => new ActualCardState(c.CardId, i == 50 ? @"Entrada\Card50" : c.ExpectedDirectory)).ToArray();
        var plan = new SyncEngine().BuildPlan(new(desired), new(true, actual, [], []));
        Assert.Single(plan.Operations); Assert.Equal(50, plan.NoChanges);
    }
    [Theory] [InlineData(@"..\Card")] [InlineData(@"Pago\CON")] [InlineData(@"C:\Card")] [InlineData(@"Pago\a/b")]
    public void RejectsUnsafePaths(string path) => Assert.False(Plan(path).CanExecute);
    [Theory] [InlineData("CON", "_CON")] [InlineData("a:b.", "a_b")] [InlineData("", "Sem nome")]
    public void SanitizesWindowsNames(string input, string expected) => Assert.Equal(expected, WindowsNames.Sanitize(input));
    [Theory] [InlineData("COM0", "_COM0")] [InlineData("lpt0.txt", "_lpt0.txt")] [InlineData("CON .txt", "_CON .txt")]
    public void SanitizesAllReservedNames(string input, string expected) => Assert.Equal(expected, WindowsNames.Sanitize(input));
    public static TheoryData<string> HostileNames => new()
    {
        new string('a', 119) + "\U0001F600", "\uD83D", "a\uDE00b", new string(' ', 130) + "x", new string('.', 130) + "x", "Concluído"
    };
    [Theory] [MemberData(nameof(HostileNames))]
    public void SanitizedNamesNeverBlockPlanAndSurviveUtf8(string input)
    {
        var name = WindowsNames.Sanitize(input);
        Assert.True(WindowsNames.IsValid(name));
        Assert.True(name.IsNormalized());
        Assert.Equal(name, System.Text.Encoding.UTF8.GetString(System.Text.Encoding.UTF8.GetBytes(name)));
    }
    [Fact] public void CaseOnlyDifferenceIsNotRenamed()
    { var plan = Plan(@"Pago\Card", @"pago\CARD"); Assert.Empty(plan.Operations); Assert.Equal(1, plan.NoChanges); }
    [Fact] public void DestinationVacatedInSamePlanStillBlocks()
    {
        var plan = new SyncEngine().BuildPlan(
            new([new("a", "Novo", "l", @"Pago\Novo"), new("b", "Antigo", "l", @"Pago\Antigo")]),
            new(true, [new("a", @"Pago\Antigo")], [], []));
        Assert.False(plan.CanExecute);
    }
    [Fact] public void FiltersByImmutableMemberId()
    { Assert.False(new AssignedMemberCardFilter("mine").Includes(new("id", "b", "l", "Card", "", null, "", ["other"], []))); }
}
