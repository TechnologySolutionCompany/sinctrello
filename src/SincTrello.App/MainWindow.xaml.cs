using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SincTrello.App.ViewModels;

namespace SincTrello.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.RestoreSessionAsync();
        Closing += (_, _) => viewModel.CancelOperation();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.K && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.Escape && viewModel.HasSelection)
        { viewModel.SelectedDemand = null; e.Handled = true; }
        else if (e.Key == Key.Enter && SearchBox.IsKeyboardFocusWithin && viewModel.Demands.Count > 0)
        { viewModel.SelectedDemand = viewModel.Demands[0]; e.Handled = true; }
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void Maximize_Click(object sender, RoutedEventArgs e)
    { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void BrowsePrimary_Click(object sender, RoutedEventArgs e) => BrowseFolder(true);
    private void BrowseBackup_Click(object sender, RoutedEventArgs e) => BrowseFolder(false);
    private void BrowseFolder(bool primary)
    {
        var dialog = new OpenFolderDialog { Title = primary ? "Escolher pasta principal" : "Escolher destino do espelho", Multiselect = false };
        if (dialog.ShowDialog(this) == true)
        { if (primary) viewModel.PrimaryPath = dialog.FolderName; else viewModel.BackupPath = dialog.FolderName; }
    }
    private async void SaveSettings_Click(object sender, RoutedEventArgs e) => await viewModel.SaveSettingsAsync();
    private void ConnectTrello_Click(object sender, RoutedEventArgs e)
    {
        if (viewModel.CanConnect) new TrelloConnectionWindow(viewModel) { Owner = this }.ShowDialog();
    }
    private void DisconnectTrello_Click(object sender, RoutedEventArgs e) => viewModel.Disconnect();
    private async void RefreshTrello_Click(object sender, RoutedEventArgs e) => await viewModel.LoadBoardAsync();
    private void CancelRead_Click(object sender, RoutedEventArgs e) => viewModel.CancelOperation();
    private void OpenCard_Click(object sender, RoutedEventArgs e)
    {
        if (!viewModel.CanOpenCard || viewModel.SelectedDemand?.CardUrl is not { } url) return;
        try { TrelloLinks.Open(TrelloLinks.CardUri(url)); }
        catch (ArgumentException ex) { viewModel.ReportNotice(ex.Message); }
        catch (Exception) { viewModel.ReportNotice("Não foi possível abrir o card no navegador padrão."); }
    }
}
