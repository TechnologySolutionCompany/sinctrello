using System.Text;

namespace SincTrello.Core;

public sealed record DesiredCardState(string CardId, string Name, string ListId, string ExpectedDirectory);
public sealed record ActualCardState(string CardId, string ActualDirectory);
public sealed record DesiredState(IReadOnlyList<DesiredCardState> Cards);
public sealed record ActualState(bool StorageAvailable, IReadOnlyList<ActualCardState> Cards,
    IReadOnlyList<string> OccupiedDirectories, IReadOnlyList<string> KnownCardIds);
public enum SyncOperationType { CreateDirectory, MoveDirectory, RenameDirectory, MoveAndRename }
public sealed record SyncOperation(SyncOperationType Type, string CardId, string? Source, string Destination);
public sealed record SyncPlan(IReadOnlyList<SyncOperation> Operations, IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Conflicts, int NoChanges)
{
    public bool CanExecute => Conflicts.Count == 0;
}
public interface ISyncEngine { SyncPlan BuildPlan(DesiredState desired, ActualState actual); }

/// <summary>Pure comparison. No disk, HTTP or database mutations.</summary>
public sealed class SyncEngine : ISyncEngine
{
    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;
    public SyncPlan BuildPlan(DesiredState desired, ActualState actual)
    {
        var operations = new List<SyncOperation>();
        var warnings = new List<string>();
        var conflicts = new List<string>();
        var unchanged = 0;
        if (!actual.StorageAvailable)
            return new([], [], ["Não foi possível acessar o local de sincronização configurado."], 0);
        if (desired.Cards.Any(c => string.IsNullOrWhiteSpace(c.CardId) || !SafePath(c.ExpectedDirectory)) ||
            actual.Cards.Any(c => string.IsNullOrWhiteSpace(c.CardId) || !SafePath(c.ActualDirectory)))
            return new([], [], ["Identidade ou caminho relativo inválido."], 0);
        foreach (var group in desired.Cards.GroupBy(c => c.CardId).Where(g => g.Count() > 1))
            conflicts.Add($"Card duplicado no estado desejado: {group.Key}");
        foreach (var group in desired.Cards.GroupBy(c => c.ExpectedDirectory, Paths).Where(g => g.Count() > 1))
            conflicts.Add($"Destino duplicado: {group.Key}");
        foreach (var group in actual.Cards.GroupBy(c => c.ActualDirectory, Paths).Where(g => g.Count() > 1))
            conflicts.Add($"Identidades sobrepostas: {group.Key}");
        if (conflicts.Count > 0) return new([], warnings, conflicts, 0);
        var byId = actual.Cards.ToLookup(c => c.CardId);
        var occupied = actual.OccupiedDirectories.Concat(actual.Cards.Select(c => c.ActualDirectory)).ToHashSet(Paths);
        var known = actual.KnownCardIds.ToHashSet(StringComparer.Ordinal);
        foreach (var card in desired.Cards)
        {
            var matches = byId[card.CardId].ToArray();
            if (matches.Length > 1) { conflicts.Add($"Identidade duplicada: {card.CardId}"); continue; }
            var current = matches.SingleOrDefault();
            if (current is not null && Paths.Equals(current.ActualDirectory, card.ExpectedDirectory))
            { unchanged++; continue; }
            if (occupied.Contains(card.ExpectedDirectory))
            { conflicts.Add($"Destino ocupado: {card.ExpectedDirectory}"); continue; }
            if (current is null)
            {
                if (known.Contains(card.CardId))
                { conflicts.Add($"Pasta conhecida não localizada: {card.CardId}. Verifique antes de recriar."); continue; }
                operations.Add(new(SyncOperationType.CreateDirectory, card.CardId, null, card.ExpectedDirectory));
                continue;
            }
            var moved = !Paths.Equals(Parent(current.ActualDirectory), Parent(card.ExpectedDirectory));
            var renamed = !Paths.Equals(Leaf(current.ActualDirectory), Leaf(card.ExpectedDirectory));
            var type = moved && renamed ? SyncOperationType.MoveAndRename : moved
                ? SyncOperationType.MoveDirectory : SyncOperationType.RenameDirectory;
            operations.Add(new(type, card.CardId, current.ActualDirectory, card.ExpectedDirectory));
        }
        var desiredIds = desired.Cards.Select(c => c.CardId).ToHashSet(StringComparer.Ordinal);
        foreach (var id in actual.Cards.Select(c => c.CardId).Concat(known).Distinct().Where(id => !desiredIds.Contains(id)))
            warnings.Add($"MissingFromSnapshot: {id}. Arquivos preservados.");
        return new(operations, warnings, conflicts, unchanged);
    }
    private static string Parent(string path) => path[..path.LastIndexOf('\\')];
    private static string Leaf(string path) => path[(path.LastIndexOf('\\') + 1)..];
    private static bool SafePath(string path) => !string.IsNullOrWhiteSpace(path) &&
        path.Split('\\') is { Length: 2 } parts && parts.All(WindowsNames.IsValid);
}

public static class WindowsNames
{
    public static bool IsValid(string name) => name.Length is > 0 and <= 120 && name is not "." and not ".." &&
        name.TrimEnd(' ', '.') == name && !name.Any(c => c < 32 || "\\/:*?\"<>|".Contains(c)) &&
        !IsReserved(name.Split('.')[0]);
    /// <summary>NFC keeps visually identical Trello names on the same folder; surrogate-safe so names survive UTF-8 persistence.</summary>
    public static string Sanitize(string name)
    {
        var normalized = ReplaceUnpairedSurrogates(name).Normalize(NormalizationForm.FormC);
        var value = new string(normalized.Select(c => c < 32 || "\\/:*?\"<>|".Contains(c) ? '_' : c).ToArray()).TrimEnd(' ', '.');
        if (IsReserved(value.Split('.')[0])) value = "_" + value;
        value = Truncate(value, 120).TrimEnd(' ', '.');
        return value.Length == 0 ? "Sem nome" : value;
    }
    internal static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value
        : value[..(char.IsHighSurrogate(value[maxLength - 1]) ? maxLength - 1 : maxLength)];
    private static string ReplaceUnpairedSurrogates(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            if (char.IsHighSurrogate(chars[i]) && i + 1 < chars.Length && char.IsLowSurrogate(chars[i + 1])) i++;
            else if (char.IsSurrogate(chars[i])) chars[i] = '_';
        return new string(chars);
    }
    private static bool IsReserved(string value)
    {
        value = value.TrimEnd(' ');
        return new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }.Contains(value, StringComparer.OrdinalIgnoreCase) ||
            (value.Length == 4 && (value.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
             value.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "0123456789¹²³".Contains(value[3]));
    }
}
