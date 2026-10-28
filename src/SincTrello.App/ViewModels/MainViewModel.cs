using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using SincTrello.Infrastructure;
using SincTrello.Infrastructure.Trello;

namespace SincTrello.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly AppSettingsStore settingsStore;
    private AppSettings savedSettings;
    private readonly IReadOnlyList<DemandItem> examples = DemoData.Create();
    private string page = "overview", query = "", listFilter = "Todas as listas", folderFilter = "Todas as pastas";
    private bool demoMode, isSaving;
    private DemandItem? selectedDemand;
    private string boardId, username, primaryPath, backupPath;
    private bool downloadAttachments, confirmPlan;
    private string notice = "Pronto para configurar. Conecte o Trello para carregar suas demandas.";

    public MainViewModel(AppSettingsStore settingsStore, AppSettings settings, TrelloSession? session = null)
    {
        this.settingsStore = settingsStore;
        this.session = session;
        savedSettings = settings;
        boardId = settings.BoardId ?? "";
        username = settings.TrelloUsername ?? "";
        primaryPath = settings.PrimaryPath ?? "";
        backupPath = settings.BackupPath ?? "";
        downloadAttachments = settings.DownloadAttachments;
        confirmPlan = settings.ConfirmPlan;
        NavigateCommand = new RelayCommand(value => Navigate(value as string ?? "overview"));
        ToggleDemoCommand = new RelayCommand(_ => ToggleDemo());
        OpenDemandCommand = new RelayCommand(value => SelectedDemand = value as DemandItem);
        CloseDemandCommand = new RelayCommand(_ => SelectedDemand = null);
        SelectListCommand = new RelayCommand(value => { Navigate("demands"); ListFilter = value as string ?? "Todas as listas"; });
        RefreshData();
    }

    public RelayCommand NavigateCommand { get; }
    public RelayCommand ToggleDemoCommand { get; }
    public RelayCommand OpenDemandCommand { get; }
    public RelayCommand CloseDemandCommand { get; }
    public RelayCommand SelectListCommand { get; }
    public ObservableCollection<DemandItem> Demands { get; } = [];
    public ObservableCollection<ListSummary> ListSummaries { get; } = [];
    public ObservableCollection<PlanRow> PlanRows { get; } = [];
    public ObservableCollection<DemandItem> RecentDemands { get; } = [];
    public IReadOnlyList<string> ListOptions => ["Todas as listas", .. SourceLists];
    public IReadOnlyList<string> FolderOptions { get; } = ["Todas as pastas", "Pendentes", "Conflitos"];
    public bool IsOverview => page == "overview";
    public bool IsDemands => page == "demands";
    public bool IsSync => page == "sync";
    public bool IsBackup => page == "backup";
    public bool IsHistory => page == "history";
    public bool IsSettings => page == "settings";
    public bool DemoMode => demoMode;
    public bool IsDisconnected => !demoMode && activeBoard is null;
    public bool HasNoDemands => Demands.Count == 0;
    public bool HasNoRecent => RecentDemands.Count == 0;
    public bool HasSelection => SelectedDemand is not null;
    public bool CanSave => !isSaving && !IsBusy;
    public string PageTitle => page switch { "demands" => "Demandas", "sync" => "Sincronizar", "backup" => "Backup", "history" => "Histórico", "settings" => "Configurações", _ => "Visão Geral" };
    public string PageQuestion => page switch { "demands" => "Onde está meu trabalho?", "sync" => "O que mudou?", "backup" => "Meu trabalho está protegido?", "history" => "O que o S.T fez?", "settings" => "Como o sistema deve funcionar?", _ => "Está tudo bem?" };
    public string ModeLabel => demoMode ? "DEMONSTRAÇÃO · DADOS DE EXEMPLO" : IsBusy ? "CONSULTANDO O TRELLO…" : readFailed && activeBoard is not null ? "LEITURA INTERROMPIDA · DADOS DA ÚLTIMA CONSULTA" : IsConnected ? "TRELLO CONECTADO · SOMENTE LEITURA" : "TRELLO DESCONECTADO · CONFIGURAÇÃO INICIAL";
    public string DemoButtonLabel => demoMode ? "Sair da demonstração" : "Explorar demonstração";
    public string Headline => demoMode ? "Há mudanças para conferir" : activeBoard is not null ? "Suas demandas estão aqui" : "Seu trabalho começa aqui";
    public string OverviewText => demoMode ? "Exemplo de 4 alterações de pastas e 1 conflito. Explore as demandas e a prévia do plano." : activeBoard is not null ? $"{liveDemands.Count} demandas em {activeBoard.Name}, filtradas pelos cards em que {connectedMember?.DisplayName} é membro." : "Conecte seu quadro e escolha a pasta principal para organizar suas demandas com o S.T.";
    public string DemandCount => demoMode || activeBoard is not null ? SourceDemands.Count.ToString(CultureInfo.InvariantCulture) : "—";
    public string PendingCount => demoMode ? "4 alterações" : activeBoard is not null ? "Pastas ainda não verificadas" : "Aguardando conexão";
    public string FilterCount => $"{Demands.Count} {(Demands.Count == 1 ? "demanda" : "demandas")}{(demoMode ? " de exemplo" : "")}";
    public string PrimaryPathLabel => string.IsNullOrWhiteSpace(savedSettings.PrimaryPath) ? "Não configurada" : savedSettings.PrimaryPath;
    public string BackupPathLabel => string.IsNullOrWhiteSpace(savedSettings.BackupPath) ? "Não configurado" : savedSettings.BackupPath;
    public string FolderConnection => string.IsNullOrWhiteSpace(savedSettings.PrimaryPath) ? "Não definida" : "A validar";
    public string MirrorConnection => string.IsNullOrWhiteSpace(savedSettings.BackupPath) ? "Não definido" : "A validar";
    public string EmptyDemandText => demoMode || activeBoard is not null ? "Nenhuma demanda corresponde à busca e aos filtros. São considerados os cards em que sua conta é membro." : "Conecte a conta, selecione um quadro e clique em Carregar demandas.";
    public string Notice { get => notice; private set => Set(ref notice, value); }
    public string Query
    {
        get => query;
        set { if (!Set(ref query, value)) return; if (!string.IsNullOrWhiteSpace(value)) Navigate("demands"); RefreshDemands(); }
    }
    public string ListFilter { get => listFilter; set { if (Set(ref listFilter, value ?? "Todas as listas")) RefreshDemands(); } }
    public string FolderFilter { get => folderFilter; set { if (Set(ref folderFilter, value ?? "Todas as pastas")) RefreshDemands(); } }
    public DemandItem? SelectedDemand
    {
        get => selectedDemand;
        set
        {
            if (!Set(ref selectedDemand, value)) return;
            Notify(nameof(HasSelection));
            Notify(nameof(CanOpenCard));
            if (value is null) return;
            RecentDemands.Remove(value);
            RecentDemands.Insert(0, value);
            if (RecentDemands.Count > 5) RecentDemands.RemoveAt(5);
            Notify(nameof(HasNoRecent));
        }
    }
    public string BoardId { get => boardId; set => Set(ref boardId, value); }
    public string Username { get => username; set => Set(ref username, value); }
    public string PrimaryPath { get => primaryPath; set => Set(ref primaryPath, value); }
    public string BackupPath { get => backupPath; set => Set(ref backupPath, value); }
    public bool DownloadAttachments { get => downloadAttachments; set => Set(ref downloadAttachments, value); }
    public bool ConfirmPlan { get => confirmPlan; set => Set(ref confirmPlan, value); }

    public void Navigate(string target)
    {
        page = target;
        SelectedDemand = null;
        foreach (var property in new[] { nameof(IsOverview), nameof(IsDemands), nameof(IsSync), nameof(IsBackup), nameof(IsHistory), nameof(IsSettings), nameof(PageTitle), nameof(PageQuestion) }) Notify(property);
    }

    private void ToggleDemo()
    {
        if (IsBusy) return;
        demoMode = !demoMode;
        query = ""; listFilter = "Todas as listas"; folderFilter = "Todas as pastas";
        SelectedDemand = null;
        RecentDemands.Clear();
        Notify(nameof(HasNoRecent));
        foreach (var property in new[] { nameof(DemoMode), nameof(IsDisconnected), nameof(ModeLabel), nameof(DemoButtonLabel), nameof(Headline), nameof(OverviewText), nameof(DemandCount), nameof(PendingCount), nameof(Query), nameof(ListOptions), nameof(ListFilter), nameof(FolderFilter), nameof(EmptyDemandText) }) Notify(property);
        Notice = demoMode ? "Demonstração ativa. Os exemplos não são dados atuais do Trello e não alteram arquivos." : "Demonstração encerrada. " + ConnectionText;
        NotifyConnectionState();
        RefreshData();
    }

    private void RefreshData()
    {
        RefreshDemands();
        ListSummaries.Clear();
        PlanRows.Clear();
        var counts = SourceDemands.GroupBy(item => item.ListName).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var max = Math.Max(1, counts.Values.DefaultIfEmpty(1).Max());
        foreach (var name in SourceLists)
        {
            var count = counts.GetValueOrDefault(name);
            ListSummaries.Add(new ListSummary(name, count, count * 144d / max));
        }
        if (!demoMode) return;
        foreach (var item in examples.Where(item => item.IsPending))
            PlanRows.Add(new PlanRow(item.FolderStatus, item.Name, item.ListName));
    }

    private static string NormalizeSearch(string value) => string.Concat(value.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)).ToUpperInvariant();

    private void RefreshDemands()
    {
        Demands.Clear();
        var search = NormalizeSearch(Query.Trim());
        foreach (var item in SourceDemands.Where(item =>
                (ListFilter == "Todas as listas" || item.ListName == ListFilter) &&
                (FolderFilter == "Todas as pastas" || (FolderFilter == "Pendentes" ? item.IsPending : item.FolderStatus == "Conflito")) &&
                NormalizeSearch($"{item.Name} {item.Client} {item.Description}").Contains(search, StringComparison.Ordinal))) Demands.Add(item);
        Notify(nameof(HasNoDemands));
        Notify(nameof(FilterCount));
    }

    public async Task SaveSettingsAsync()
    {
        if (isSaving || IsBusy) return;
        isSaving = true;
        Notify(nameof(CanSave));
        try
        {
            var primary = CleanPath(PrimaryPath);
            var backup = CleanPath(BackupPath);
            if (primary is not null && backup is not null &&
                (IsInside(primary, backup) || IsInside(backup, primary)))
                throw new ArgumentException("A pasta principal e o espelho devem ser separados, sem uma dentro da outra.");
            var settings = savedSettings with
            {
                BoardId = EmptyToNull(BoardId), TrelloUsername = connectedMember?.Username ?? EmptyToNull(Username),
                TrelloMemberId = connectedMember?.Id ?? (Username.Trim() == (savedSettings.TrelloUsername ?? "") ? savedSettings.TrelloMemberId : null),
                PrimaryPath = primary, BackupPath = backup,
                DownloadAttachments = DownloadAttachments, ConfirmPlan = ConfirmPlan
            };
            await settingsStore.SaveAsync(settings);
            savedSettings = settings;
            foreach (var property in new[] { nameof(PrimaryPathLabel), nameof(BackupPathLabel), nameof(FolderConnection), nameof(MirrorConnection) }) Notify(property);
            Notice = "Preferências salvas nesta máquina. " + ConnectionText;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        { Notice = ex.Message; }
        catch (Exception)
        { Notice = "Não foi possível salvar. Verifique as permissões e o espaço em disco e tente novamente."; }
        finally { isSaving = false; Notify(nameof(CanSave)); }
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? CleanPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Path.IsPathFullyQualified(value.Trim())) throw new ArgumentException("Informe um caminho completo para as pastas, como D:\\Demandas.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value.Trim()));
    }
    private static bool IsInside(string parent, string child) => string.Equals(parent, child, StringComparison.OrdinalIgnoreCase) ||
        child.StartsWith(Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
