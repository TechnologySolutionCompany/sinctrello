using System.Text.RegularExpressions;
using SincTrello.Core;

namespace SincTrello.App.ViewModels;

public static partial class DemandMapper
{
    public static DemandItem FromCard(CardSnapshot card, string listName)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in card.Description.Split('\n'))
        {
            var line = raw.Replace("**", "", StringComparison.Ordinal).Trim().TrimStart('#', ' ', '-', '•');
            var colon = line.IndexOf(':');
            if (colon > 0) fields.TryAdd(line[..colon].Trim(), line[(colon + 1)..].Trim());
        }
        var title = SscTitle().Match(card.Name);
        var ssc = title.Success ? title.Groups[1].Value : fields.GetValueOrDefault("Número SSC", "—");
        var client = fields.GetValueOrDefault("Cliente", title.Success ? title.Groups[2].Value.Trim() : card.Name);
        var description = fields.GetValueOrDefault("Descrição da SSC", "");
        if (string.IsNullOrWhiteSpace(description))
        {
            var lines = card.Description.Replace("**", "", StringComparison.Ordinal).Split('\n');
            var heading = Array.FindIndex(lines, l => l.Trim().Equals("Descrição da SSC:", StringComparison.OrdinalIgnoreCase));
            if (heading >= 0) description = lines.Skip(heading + 1).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim() ?? "";
        }
        if (string.IsNullOrWhiteSpace(description)) description = card.Description.Trim();
        return new(ssc, client, description, listName, fields.GetValueOrDefault("Prioridade", "—"), card.Attachments.Count, "Não verificada")
        {
            CardId = card.Id, OriginalName = card.Name, CardUrl = card.Url,
            FullDescription = card.Description, Technician = fields.GetValueOrDefault("Técnico", "")
        };
    }

    [GeneratedRegex(@"^\s*SSC\s*(\d+)\s*[-–—]\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SscTitle();
}
