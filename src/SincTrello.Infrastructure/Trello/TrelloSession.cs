using SincTrello.Core;

namespace SincTrello.Infrastructure.Trello;

public sealed record TrelloConnectionResult(MemberSnapshot Member, IReadOnlyList<TrelloBoardChoice> Boards);

public sealed class TrelloSession(ITrelloCredentialStore store, ITrelloClientFactory factory)
{
    public ITrelloReadClient? Client { get; private set; }
    public bool HasCredential { get; private set; }

    public async Task<TrelloConnectionResult> ConnectAsync(TrelloCredentials credentials, CancellationToken cancellationToken)
    {
        var (client, result) = await ValidateAsync(credentials, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        store.Save(credentials);
        HasCredential = true;
        Client = client;
        return result;
    }

    public async Task<TrelloConnectionResult?> RestoreAsync(CancellationToken cancellationToken)
    {
        var credentials = store.Read();
        if (credentials is null) return null;
        HasCredential = true;
        var (client, result) = await ValidateAsync(credentials, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        Client = client;
        return result;
    }

    private async Task<(ITrelloReadClient Client, TrelloConnectionResult Result)> ValidateAsync(TrelloCredentials credentials, CancellationToken cancellationToken)
    {
        var client = factory.Create(credentials);
        var member = await client.GetCurrentMemberAsync(cancellationToken).ConfigureAwait(false);
        var boards = await client.GetBoardsAsync(cancellationToken).ConfigureAwait(false);
        return (client, new TrelloConnectionResult(member, boards));
    }

    public void Disconnect() { store.Delete(); Client = null; HasCredential = false; }
}
