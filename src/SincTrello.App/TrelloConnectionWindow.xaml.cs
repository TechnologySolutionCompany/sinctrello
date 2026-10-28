using System.Windows;
using SincTrello.App.ViewModels;
using SincTrello.Infrastructure.Trello;

namespace SincTrello.App;

public partial class TrelloConnectionWindow : Window
{
    private readonly MainViewModel viewModel;
    private bool closed;
    public TrelloConnectionWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        DataContext = viewModel;
        Closed += (_, _) => { closed = true; viewModel.CancelOperation(); TokenInput.Clear(); };
    }
    private void OpenAdmin_Click(object sender, RoutedEventArgs e) => OpenBrowser(new Uri("https://trello.com/apps/admin"));
    private void Authorize_Click(object sender, RoutedEventArgs e)
    {
        try { OpenBrowser(TrelloLinks.AuthorizationUri(ApiKeyInput.Text)); }
        catch (ArgumentException ex) { viewModel.ReportNotice(ex.Message); }
    }
    private void OpenBrowser(Uri uri)
    {
        try { TrelloLinks.Open(uri); }
        catch (Exception) { viewModel.ReportNotice("Não foi possível abrir o navegador padrão. Verifique a configuração do Windows."); }
    }
    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var credentials = new TrelloCredentials(ApiKeyInput.Text, TokenInput.Password);
            if (await viewModel.ConnectAsync(credentials) && !closed) DialogResult = true;
        }
        catch (ArgumentException ex) { viewModel.ReportNotice(ex.Message); }
        finally { TokenInput.Clear(); }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { viewModel.CancelOperation(); Close(); }
}
