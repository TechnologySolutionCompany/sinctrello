namespace SincTrello.Core;

public static class DesiredStateBuilder
{
    public static DesiredState Build(BoardSnapshot board, ICardOwnershipFilter filter)
    {
        var lists = board.Lists.ToDictionary(l => l.Id);
        var listNames = board.Lists.GroupBy(l => WindowsNames.Sanitize(l.Name), StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group.Select(l => new { l.Id, Name = group.Count() > 1
                ? WithId(WindowsNames.Sanitize(l.Name), l.Id) : WindowsNames.Sanitize(l.Name) }))
            .ToDictionary(l => l.Id, l => l.Name);
        var cards = board.Cards.Where(filter.Includes).ToArray();
        if (cards.Any(c => c.BoardId != board.Id || !lists.ContainsKey(c.ListId)))
            throw new InvalidOperationException("Snapshot inconsistente: quadro ou lista desconhecida.");
        var result = cards.GroupBy(c => listNames[c.ListId] + "\\" + WindowsNames.Sanitize(c.Name), StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => group.Select(c => new DesiredCardState(c.Id, c.Name, c.ListId,
                listNames[c.ListId] + "\\" + (group.Count() > 1
                    ? WithId(WindowsNames.Sanitize(c.Name), c.Id) : WindowsNames.Sanitize(c.Name)))))
            .ToArray();
        return new(result);
    }
    private static string WithId(string name, string id)
    {
        if (id.Length is < 1 or > 64 || !id.All(char.IsAsciiLetterOrDigit))
            throw new InvalidOperationException("Identificador Trello inválido para desambiguação.");
        var suffix = " [" + id + "]";
        return WindowsNames.Truncate(name, 120 - suffix.Length) + suffix;
    }
}
