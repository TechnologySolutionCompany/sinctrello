using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using SincTrello.Core;
using SincTrello.Infrastructure.Trello;

namespace SincTrello.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly TrelloSession? session;
    private MemberSnapshot? connectedMember;
    private BoardSnapshot? activeBoard;
    private IReadOnlyList<DemandItem> liveDemands = [];
    private TrelloBoardChoice? selectedBoard;
    private CancellationTokenSource? operation;
    private bool isConnected, readFailed;
    private DateTimeOffset? lastRead;

    private IReadOnlyList<DemandItem> SourceDemands => demoMode ? examples : liveDemands;
    private IReadOnlyList<string> SourceLists => demoMode ? DemoData.Lists : activeBoard?.Lists.Select(l => l.Name).Distinct().ToArray() ?? [];
    public ObservableCollection<TrelloBoardChoice> Boards { get; } = [];
    public bool IsBusy => operation is not null;
    public bool IsIdle => !IsBusy;
    public bool IsConnected => isConnected;
    public bool CanConnect => session is not null && IsIdle;
    public bool CanDisconnect => session?.HasCredential == true && IsIdle;
    public bool CanSelectBoard => IsConnected && IsIdle;
    public bool CanRefresh => IsConnected && IsIdle && SelectedBoard is not null && !DemoMode;
    public bool HasLiveData => activeBoard is not null && !DemoMode;
    public bool CanOpenCard => !DemoMode && SelectedDemand?.CardUrl is not null;
    public string TrelloConnectionLabel => IsConnected ? readFailed ? "Revisar conexão" : "Conectado" : "Desconectado";
    public string ConnectionText => IsConnected ? $"Conectado como {connectedMember?.DisplayName} (@{connectedMember?.Username}). Acesso de leitura." : "Conta ainda não conectada.";
    public string CurrentBoardName => activeBoard?.Name ?? "Nenhum quadro carregado";
    public string LastReadLabel => lastRead?.ToLocalTime().ToString("dd/MM HH:mm") ?? "Ainda não realizada";
    public string DetailKicker => DemoMode ? "DEMANDA · EXEMPLO" : "DEMANDA · TRELLO";
    public string DetailNote => DemoMode ? "Dados da referência visual. Nenhuma pasta foi lida." : "Card lido do Trello. A pasta local ainda não foi verificada; a organização de arquivos será a próxima etapa.";
    public string AttachmentNote => DemoMode ? "Quantidade de anexos do exemplo. Nenhum arquivo foi baixado." : "Quantidade real de anexos do card. O download será adicionado na etapa de sincronização.";
    public TrelloBoardChoice? SelectedBoard
    {
        get => selectedBoard;
        set
        {
            if (!Set(ref selectedBoard, value)) return;
            if (value is not null)
            {
                BoardId = value.Id;
                if (activeBoard is not null && activeBoard.Id != value.Id) ClearLiveData();
            }
            Notify(nameof(CanRefresh));
        }
    }

    public async Task<bool> ConnectAsync(TrelloCredentials credentials)
    {
        if (!CanConnect) return false;
        return await RunReadAsync(async token =>
        {
            var result = await session!.ConnectAsync(credentials, token);
            ApplyConnection(result);
            Notice = Boards.Count == 0 ? "Conta validada, mas nenhum quadro aberto foi encontrado para esta conta." : "Conta conectada. Selecione um quadro e clique em Carregar demandas.";
        });
    }

    public async Task RestoreSessionAsync()
    {
        if (!CanConnect) return;
        var restored = false;
        await RunReadAsync(async token =>
        {
            var result = await session!.RestoreAsync(token);
            if (result is null) { Notice = "Conecte o Trello para carregar suas demandas."; return; }
            ApplyConnection(result);
            restored = true;
            Notice = "Conexão restaurada. Escolha o quadro que deseja carregar.";
        });
        if (restored && SelectedBoard is not null) await LoadBoardAsync();
    }

    private void ApplyConnection(TrelloConnectionResult result)
    {
        if (connectedMember?.Id != result.Member.Id) ClearLiveData();
        connectedMember = result.Member;
        isConnected = true;
        demoMode = false;
        Username = result.Member.Username;
        Boards.Clear();
        foreach (var board in result.Boards) Boards.Add(board);
        SelectedBoard = Boards.FirstOrDefault(b => b.Id == savedSettings.BoardId);
        ResetFilters();
        NotifyConnectionState();
        RefreshData();
    }

    public async Task LoadBoardAsync()
    {
        if (!CanRefresh || session?.Client is not { } client || connectedMember is not { } member || SelectedBoard is not { } choice) return;
        await RunReadAsync(async token =>
        {
            Notice = $"Lendo listas e cards de {choice.Name}…";
            var snapshot = await client.GetBoardAsync(choice.Id, token);
            var lists = snapshot.Lists.ToDictionary(l => l.Id, l => l.Name, StringComparer.Ordinal);
            var filter = new AssignedMemberCardFilter(member.Id);
            var items = snapshot.Cards.Where(filter.Includes).Select(card => DemandMapper.FromCard(card, lists[card.ListId])).ToArray();
            token.ThrowIfCancellationRequested();
            var settings = savedSettings with { BoardId = snapshot.Id, TrelloUsername = member.Username, TrelloMemberId = member.Id };
            await settingsStore.SaveAsync(settings, token);
            savedSettings = settings;
            activeBoard = snapshot;
            liveDemands = items;
            BoardId = snapshot.Id;
            lastRead = DateTimeOffset.Now;
            ResetFilters();
            RefreshData();
            NotifyConnectionState();
            Notice = $"{items.Length} {(items.Length == 1 ? "demanda carregada" : "demandas carregadas")} de {snapshot.Name}. Filtro: cards em que você é membro. Nenhuma pasta foi alterada.";
        });
    }

    public void Disconnect()
    {
        if (!CanDisconnect) return;
        try
        {
            session!.Disconnect();
            isConnected = false;
            connectedMember = null;
            Boards.Clear(); SelectedBoard = null;
            ClearLiveData();
            Notice = "Desconectado. A credencial foi removida desta máquina. O acesso pode ser revogado também na sua conta Trello.";
            NotifyConnectionState();
        }
        catch (Exception) { Notice = "Não foi possível remover a credencial do Windows. Tente novamente."; }
    }

    public void CancelOperation() => operation?.Cancel();
    public void ReportNotice(string message) => Notice = message;

    private void ClearLiveData()
    {
        activeBoard = null; liveDemands = []; lastRead = null;
        ResetFilters(); RefreshData(); NotifyConnectionState();
    }

    private void ResetFilters()
    {
        query = ""; listFilter = "Todas as listas"; folderFilter = "Todas as pastas";
        SelectedDemand = null; RecentDemands.Clear();
        foreach (var name in new[] { nameof(Query), nameof(ListOptions), nameof(ListFilter), nameof(FolderFilter), nameof(HasNoRecent) }) Notify(name);
    }

    private async Task<bool> RunReadAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy) return false;
        operation = new CancellationTokenSource();
        NotifyConnectionState();
        try
        {
            await action(operation.Token);
            readFailed = false;
            return true;
        }
        catch (OperationCanceledException)
        { readFailed = true; Notice = "Consulta cancelada. Os dados da última leitura foram preservados."; }
        catch (TrelloApiException ex)
        {
            readFailed = true;
            if (ex.StatusCode == HttpStatusCode.Unauthorized) isConnected = false;
            Notice = ex.Message;
        }
        catch (Exception ex) when (ex is Win32Exception or System.Text.Json.JsonException or System.IO.InvalidDataException)
        { readFailed = true; Notice = "Não foi possível acessar a credencial protegida pelo Windows. Conecte novamente."; }
        catch (Exception)
        { readFailed = true; Notice = "Não foi possível concluir a consulta. Verifique a conexão e as permissões locais e tente novamente."; }
        finally
        {
            operation.Dispose(); operation = null;
            NotifyConnectionState();
        }
        return false;
    }

    private void NotifyConnectionState()
    {
        foreach (var name in new[] { nameof(IsConnected), nameof(IsBusy), nameof(IsIdle), nameof(CanConnect), nameof(CanDisconnect), nameof(CanSelectBoard), nameof(CanRefresh), nameof(CanSave), nameof(CanOpenCard), nameof(TrelloConnectionLabel), nameof(ConnectionText), nameof(ModeLabel), nameof(IsDisconnected), nameof(Headline), nameof(OverviewText), nameof(DemandCount), nameof(PendingCount), nameof(LastReadLabel), nameof(CurrentBoardName), nameof(HasLiveData), nameof(DemoMode), nameof(DemoButtonLabel), nameof(EmptyDemandText), nameof(DetailKicker), nameof(DetailNote), nameof(AttachmentNote) }) Notify(name);
    }
}
