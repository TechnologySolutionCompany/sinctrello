using SincTrello.Core;
using Xunit;

namespace SincTrello.Core.Tests;

public class DesiredStateTests
{
    private static CardSnapshot Card(string id, string name, string member = "mine") =>
        new(id, "b", "l", name, "", null, "", [member], []);

    [Fact]
    public void SameNamesHaveDistinctDeterministicPaths()
    {
        CardSnapshot[] cards = [Card("a", "Card"), Card("b", "Card")];
        var first = DesiredStateBuilder.Build(new("b", "Board", [new("l", "Pago")], cards), new AssignedMemberCardFilter("mine"));
        var reversed = DesiredStateBuilder.Build(new("b", "Board", [new("l", "Pago")], cards.Reverse().ToArray()), new AssignedMemberCardFilter("mine"));
        Assert.Equal(2, first.Cards.Select(c => c.ExpectedDirectory).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(first.Cards.OrderBy(c => c.CardId), reversed.Cards.OrderBy(c => c.CardId));
    }
    [Fact]
    public void CanonicallyEquivalentNamesAreDisambiguated()
    {
        var desired = DesiredStateBuilder.Build(new("b", "Board", [new("l", "Pago")],
            [Card("a", "Concluído"), Card("b", "Concluído")]), new AssignedMemberCardFilter("mine"));
        Assert.Equal([@"Pago\Concluído [a]", @"Pago\Concluído [b]"], desired.Cards.Select(c => c.ExpectedDirectory).Order());
    }
    [Fact]
    public void CollisionSuffixDoesNotSplitSurrogatePair()
    {
        const string id1 = "0123456789abcdef01234567", id2 = "0123456789abcdef01234568";
        var name = new string('x', 92) + "\U0001F600tail";
        var desired = DesiredStateBuilder.Build(new("b", "Board", [new("l", "Pago")], [Card(id1, name), Card(id2, name)]),
            new AssignedMemberCardFilter("mine"));
        Assert.All(desired.Cards, c => Assert.True(c.ExpectedDirectory.IsNormalized() &&
            WindowsNames.IsValid(c.ExpectedDirectory.Split('\\')[1])));
    }
    [Fact]
    public void OtherMemberDoesNotProduceDirectory()
    {
        var desired = DesiredStateBuilder.Build(new("b", "Board", [new("l", "Pago")], [Card("a", "Card", "other")]), new AssignedMemberCardFilter("mine"));
        Assert.Empty(new SyncEngine().BuildPlan(desired, new(true, [], [], [])).Operations);
    }
    [Fact]
    public void TrelloContractOnlyExposesApprovedReads()
    {
        Assert.Equal(new[] { "GetBoardAsync", "ResolveMemberAsync" }, typeof(ITrelloService).GetMethods().Select(m => m.Name).OrderBy(n => n));
    }
}
