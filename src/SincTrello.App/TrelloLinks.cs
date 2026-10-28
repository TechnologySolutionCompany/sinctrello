using System.Diagnostics;
using SincTrello.Infrastructure.Trello;

namespace SincTrello.App;

public static class TrelloLinks
{
    public static Uri AuthorizationUri(string apiKey)
    {
        var key = new TrelloCredentials(apiKey, "validation-only").ApiKey;
        return new Uri("https://trello.com/1/authorize?expiration=30days&scope=read&response_type=token&name=SincTrello&key=" + Uri.EscapeDataString(key));
    }
    public static Uri CardUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "trello.com" ||
            !uri.IsDefaultPort || uri.UserInfo.Length > 0 || !uri.AbsolutePath.StartsWith("/c/", StringComparison.Ordinal))
            throw new ArgumentException("O card não possui um link Trello válido.");
        return new Uri(uri.GetLeftPart(UriPartial.Path));
    }
    public static void Open(Uri uri) => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
}
