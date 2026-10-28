using Microsoft.Extensions.DependencyInjection;
using SincTrello.Core;
using SincTrello.Infrastructure.Trello;
using SincTrello.Infrastructure.Storage;

namespace SincTrello.Infrastructure;

public sealed record AppSettings(string? BoardId = null, string? TrelloUsername = null,
    string? TrelloMemberId = null, string? PrimaryPath = null, string? BackupPath = null,
    bool DownloadAttachments = false, bool ConfirmPlan = true);

public static class Services
{
    public static IServiceCollection AddSincTrello(this IServiceCollection services, string databasePath)
    {
        services.AddLogging();
        services.AddSingleton<ISyncEngine, SyncEngine>();
        services.AddSingleton(new LocalDatabase(databasePath));
        services.AddSingleton(new AppSettingsStore(databasePath));
        services.AddSingleton<IFileSystemService, SafeLocalFileSystem>();
        services.AddSingleton(new LocalSyncRepository(databasePath));
        services.AddSingleton<LocalFolderSyncService>();
        services.AddSingleton<ITrelloCredentialStore>(_ => OperatingSystem.IsWindows()
            ? new WindowsTrelloCredentialStore()
            : throw new PlatformNotSupportedException("A proteção de credenciais requer Windows."));
        services.AddSingleton(_ => new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, ConnectTimeout = TimeSpan.FromSeconds(15) }) { Timeout = Timeout.InfiniteTimeSpan });
        services.AddSingleton<ITrelloClientFactory, TrelloClientFactory>();
        services.AddSingleton<TrelloSession>();
        return services;
    }
}
