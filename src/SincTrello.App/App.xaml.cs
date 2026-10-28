using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SincTrello.Infrastructure;

namespace SincTrello.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? services;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var directory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SincTrello");
            System.IO.Directory.CreateDirectory(directory);
            services = new ServiceCollection().AddSincTrello(System.IO.Path.Combine(directory, "sinctrello.db")).BuildServiceProvider();
            await services.GetRequiredService<LocalDatabase>().InitializeAsync();
            var settingsStore = services.GetRequiredService<AppSettingsStore>();
            var settings = await settingsStore.LoadAsync();
            MainWindow = new MainWindow(new ViewModels.MainViewModel(settingsStore, settings, services.GetRequiredService<SincTrello.Infrastructure.Trello.TrelloSession>()));
            MainWindow.Show();
        }
        catch (Exception)
        {
            MessageBox.Show("Não foi possível iniciar o SincTrello. Verifique as configurações locais, as permissões e o espaço em disco.", "SincTrello");
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { services?.Dispose(); base.OnExit(e); }
}
