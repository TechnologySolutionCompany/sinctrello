using System.Net;
using System.IO;
using Microsoft.Data.Sqlite;
using SincTrello.App.ViewModels;
using SincTrello.Core;
using SincTrello.Infrastructure;
using SincTrello.Infrastructure.Trello;
using Xunit;

namespace SincTrello.App.Tests;

public sealed class TrelloFlowTests
{
    private static readonly TrelloCredentials Credentials = new("fake-key", "fake-token");

    [Fact]
    public async Task ConnectLoadsOnlyCardsAssignedToTheAuthenticatedMemberAndPersistsStableIds()
    {
        await WithApp(async (vm, client, store, credentials) =>
        {
            Assert.True(await vm.ConnectAsync(Credentials));
            Assert.True(vm.IsConnected);
            Assert.NotNull(credentials.Value);
            Assert.Null(vm.SelectedBoard);
            vm.SelectedBoard = vm.Boards[0];
            await vm.LoadBoardAsync();
            var item = Assert.Single(vm.Demands);
            Assert.Equal("mine-card", item.CardId);
            Assert.Equal("Não verificada", item.FolderStatus);
            Assert.DoesNotContain("Exemplo", vm.ModeLabel);
            var settings = await store.LoadAsync();
            Assert.Equal("board-a", settings.BoardId);
            Assert.Equal(client.Member.Id, settings.TrelloMemberId);
            Assert.Equal(client.Member.Username, settings.TrelloUsername);
            vm.SelectedDemand = item;
            Assert.True(vm.CanOpenCard);
        });
    }

    [Fact]
    public async Task FailedOrCancelledRefreshPreservesTheLastSuccessfulCards()
    {
        await WithApp(async (vm, client, _, _) =>
        {
            await vm.ConnectAsync(Credentials);
            vm.SelectedBoard = vm.Boards[0];
            await vm.LoadBoardAsync();
            var original = Assert.Single(vm.Demands);
            client.GetBoard = _ => throw new TrelloApiException("Sem rede");
            await vm.LoadBoardAsync();
            Assert.Same(original, Assert.Single(vm.Demands));
            Assert.Contains("DADOS DA ÚLTIMA CONSULTA", vm.ModeLabel);
            client.GetBoard = async token => { await Task.Delay(Timeout.Infinite, token); return client.Board; };
            var pending = vm.LoadBoardAsync();
            Assert.True(vm.IsBusy);
            Assert.False(vm.CanRefresh);
            vm.CancelOperation();
            await pending;
            Assert.Same(original, Assert.Single(vm.Demands));
            Assert.False(vm.IsBusy);
            Assert.Contains("cancelada", vm.Notice);
        });
    }

    [Fact]
    public async Task InvalidCredentialsDoNotReplaceTheSavedCredentialOrClaimConnection()
    {
        await WithApp(async (vm, client, _, credentials) =>
        {
            client.FailIdentity = true;
            Assert.False(await vm.ConnectAsync(Credentials));
            Assert.False(vm.IsConnected);
            Assert.Null(credentials.Value);
            Assert.Empty(vm.Demands);
        });
    }

    [Fact]
    public async Task AccountSwitchClearsPreviousUsersCardsAndDisconnectRemovesTheLocalCredential()
    {
        await WithApp(async (vm, client, _, credentials) =>
        {
            await vm.ConnectAsync(Credentials);
            vm.SelectedBoard = vm.Boards[0];
            await vm.LoadBoardAsync();
            Assert.Single(vm.Demands);
            client.Member = new("other", "other-account", "Outra Pessoa");
            await vm.ConnectAsync(new("other-key", "other-token"));
            Assert.Empty(vm.Demands);
            vm.SelectedBoard = vm.Boards[0];
            await vm.LoadBoardAsync();
            Assert.Equal("other-card", Assert.Single(vm.Demands).CardId);
            vm.Disconnect();
            Assert.False(vm.IsConnected);
            Assert.Empty(vm.Demands);
            Assert.Null(credentials.Value);
        });
    }

    [Fact]
    public async Task SessionRestoreLoadsTheSavedBoardAndKeepsDemoDataIsolated()
    {
        await WithApp(async (vm, client, settings, credentials) =>
        {
            credentials.Value = Credentials;
            await vm.RestoreSessionAsync();
            Assert.True(vm.IsConnected);
            Assert.Null(vm.SelectedBoard);
            vm.SelectedBoard = vm.Boards[0];
            await vm.LoadBoardAsync();
            var real = Assert.Single(vm.Demands);
            var reopened = new MainViewModel(settings, await settings.LoadAsync(), new TrelloSession(credentials, new Factory(client)));
            await reopened.RestoreSessionAsync();
            Assert.True(reopened.IsConnected);
            Assert.Equal("board-a", reopened.SelectedBoard?.Id);
            Assert.Equal(real.CardId, Assert.Single(reopened.Demands).CardId);
            vm.ToggleDemoCommand.Execute(null);
            Assert.Equal(13, vm.Demands.Count);
            Assert.False(vm.CanOpenCard);
            vm.ToggleDemoCommand.Execute(null);
            Assert.Same(real, Assert.Single(vm.Demands));
            Assert.Empty(vm.PlanRows);
        });
    }

    [Fact]
    public async Task RevokedTokenPreservesCardsAndStillAllowsRemovingStoredCredentials()
    {
        await WithApp(async (vm, client, _, credentials) =>
        {
            await vm.ConnectAsync(Credentials);
            vm.SelectedBoard = vm.Boards[0];
            await vm.LoadBoardAsync();
            client.GetBoard = _ => throw new TrelloApiException("Token revogado", HttpStatusCode.Unauthorized);
            await vm.LoadBoardAsync();
            Assert.False(vm.IsConnected);
            Assert.Single(vm.Demands);
            Assert.True(vm.CanDisconnect);
            vm.Disconnect();
            Assert.Null(credentials.Value);
            Assert.Empty(vm.Demands);
        });
    }

    [Fact]
    public void DisplayMappingUnderstandsDescriptionFieldsWithoutChangingCardIdentityOrName()
    {
        var card = new CardSnapshot("id", "board", "list", "SSC 1000101 - Cliente original", "https://trello.com/c/aBcD1234/card", "aBcD1234",
            "**Técnico:** Pessoa\n**Cliente:** Cliente cadastrado\n**Prioridade:** Média\n**Descrição da SSC:**\nRelação de empréstimos\n\nÚltimo Trâmite:\nEm análise", ["mine"], []);
        var item = DemandMapper.FromCard(card, "Em Andamento");
        Assert.Equal("1000101", item.Ssc);
        Assert.Equal("Cliente cadastrado", item.Client);
        Assert.Equal("Média", item.Priority);
        Assert.Equal("Relação de empréstimos", item.Description);
        Assert.Equal(card.Id, item.CardId);
        Assert.Equal(card.Name, item.Name);
        Assert.Equal(card.Description, item.FullDescription);
    }

    [Fact]
    public void BrowserLinksRequestReadOnlyAccessAndCannotOpenOtherSitesAsCards()
    {
        var authorization = TrelloLinks.AuthorizationUri("fake-key");
        Assert.Equal("trello.com", authorization.Host);
        Assert.Contains("scope=read&", authorization.Query);
        Assert.DoesNotContain("write", authorization.Query);
        Assert.Throws<ArgumentException>(() => TrelloLinks.CardUri("https://evil.test/c/aBcD1234"));
        Assert.Throws<ArgumentException>(() => TrelloLinks.CardUri("file:///C:/Windows/System32/cmd.exe"));
        Assert.Equal("https://trello.com/c/aBcD1234/card", TrelloLinks.CardUri("https://trello.com/c/aBcD1234/card?tracking=value").AbsoluteUri);
    }

    private static async Task WithApp(Func<MainViewModel, Client, AppSettingsStore, CredentialStore, Task> assertion)
    {
        var path = Path.Combine(Path.GetTempPath(), $"st-app-{Guid.NewGuid():N}.db");
        try
        {
            await new LocalDatabase(path).InitializeAsync();
            var settings = new AppSettingsStore(path);
            var client = new Client();
            var credentialStore = new CredentialStore();
            var session = new TrelloSession(credentialStore, new Factory(client));
            await assertion(new MainViewModel(settings, new AppSettings(), session), client, settings, credentialStore);
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
    private sealed class CredentialStore : ITrelloCredentialStore
    {
        public TrelloCredentials? Value { get; set; }
        public TrelloCredentials? Read() => Value;
        public void Save(TrelloCredentials credentials) => Value = credentials;
        public void Delete() => Value = null;
    }
    private sealed class Factory(Client client) : ITrelloClientFactory { public ITrelloReadClient Create(TrelloCredentials credentials) => client; }
    private sealed class Client : ITrelloReadClient
    {
        public MemberSnapshot Member { get; set; } = new("mine", "my-account", "Minha Conta");
        public bool FailIdentity { get; set; }
        public Func<CancellationToken, Task<BoardSnapshot>>? GetBoard { get; set; }
        public BoardSnapshot Board { get; } = new("board-a", "Quadro de Teste", [new("list-a", "Em Andamento")],
        [
            new("mine-card", "board-a", "list-a", "SSC 1 - Meu Cliente", "https://trello.com/c/aBcD1234/card", "aBcD1234", "Prioridade: Alta", ["mine"], []),
            new("other-card", "board-a", "list-a", "SSC 2 - Outro Cliente", "https://trello.com/c/zYxW1234/card", "zYxW1234", "", ["other"], [])
        ]);
        public Task<MemberSnapshot> GetCurrentMemberAsync(CancellationToken cancellationToken) => FailIdentity ? throw new TrelloApiException("Recusado", HttpStatusCode.Unauthorized) : Task.FromResult(Member);
        public Task<MemberSnapshot> ResolveMemberAsync(string username, CancellationToken cancellationToken) => GetCurrentMemberAsync(cancellationToken);
        public Task<IReadOnlyList<TrelloBoardChoice>> GetBoardsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TrelloBoardChoice>>([new("board-a", "Quadro de Teste", "https://trello.com/b/aBcD1234")]);
        public Task<BoardSnapshot> GetBoardAsync(string boardId, CancellationToken cancellationToken) => GetBoard?.Invoke(cancellationToken) ?? Task.FromResult(Board);
    }
}
