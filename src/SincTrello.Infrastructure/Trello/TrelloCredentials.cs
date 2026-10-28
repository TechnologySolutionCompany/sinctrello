namespace SincTrello.Infrastructure.Trello;

public sealed class TrelloCredentials
{
    public string ApiKey { get; }
    public string Token { get; }
    public TrelloCredentials(string apiKey, string token)
    {
        ApiKey = Validate(apiKey, 256);
        Token = Validate(token, 1024);
    }
    private static string Validate(string value, int maxLength)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.Length > maxLength ||
            trimmed.Any(c => c < '!' || c > '~' || c is '"' or '\\'))
            throw new ArgumentException("Informe uma chave e um token Trello válidos, sem espaços ou quebras de linha.");
        return trimmed;
    }
    public override string ToString() => "Credenciais Trello [protegidas]";
}

public interface ITrelloCredentialStore
{
    TrelloCredentials? Read();
    void Save(TrelloCredentials credentials);
    void Delete();
}
