using SincTrello.Core;

namespace SincTrello.Infrastructure.Trello;

public sealed record TrelloBoardChoice(string Id, string Name, string Url);
public interface ITrelloReadClient : ITrelloService
{
    Task<MemberSnapshot> GetCurrentMemberAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<TrelloBoardChoice>> GetBoardsAsync(CancellationToken cancellationToken);
}

public interface ITrelloClientFactory
{
    ITrelloReadClient Create(TrelloCredentials credentials);
}

public sealed class TrelloClientFactory(HttpClient httpClient) : ITrelloClientFactory
{
    public ITrelloReadClient Create(TrelloCredentials credentials) => new TrelloReadService(httpClient, credentials);
}

public sealed class TrelloApiException(string message, System.Net.HttpStatusCode? statusCode = null) : Exception(message)
{
    public System.Net.HttpStatusCode? StatusCode { get; } = statusCode;
}
