using Microsoft.Data.Sqlite;
using SincTrello.Infrastructure;
using Xunit;

namespace SincTrello.Infrastructure.Tests;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public async Task EmptyDatabaseUsesSafeDefaultsAndLegacyJsonRemainsReadable()
    {
        await WithDatabase(async (path, store) =>
        {
            var defaults = await store.LoadAsync();
            Assert.Equal(new AppSettings(), defaults);
            Assert.True(defaults.ConfirmPlan);
            Assert.False(defaults.DownloadAttachments);
            using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO AppSettings VALUES (1, '{}')";
            await command.ExecuteNonQueryAsync();
            Assert.Equal(defaults, await store.LoadAsync());
        });
    }

    [Fact]
    public async Task SavesAndReplacesPreferencesWithoutDuplicatingTheRow()
    {
        await WithDatabase(async (path, store) =>
        {
            var first = new AppSettings("board-1", "técnico'1", "member-1", @"D:\Demandas", @"E:\Espelho", true, false);
            await store.SaveAsync(first);
            Assert.Equal(first, await new AppSettingsStore(path).LoadAsync());
            var updated = first with { PrimaryPath = @"F:\Demandas", ConfirmPlan = true };
            await store.SaveAsync(updated);
            Assert.Equal(updated, await store.LoadAsync());
            using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM AppSettings";
            Assert.Equal(1L, await command.ExecuteScalarAsync());
        });
    }

    [Fact]
    public async Task MalformedPreferencesFailExplicitlyWithoutOverwritingTheStoredValue()
    {
        await WithDatabase(async (path, store) =>
        {
            using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO AppSettings VALUES (1, 'invalid-json')";
            await command.ExecuteNonQueryAsync();
            await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => store.LoadAsync());
            command.CommandText = "SELECT Json FROM AppSettings WHERE Id = 1";
            Assert.Equal("invalid-json", await command.ExecuteScalarAsync());
        });
    }

    private static async Task WithDatabase(Func<string, AppSettingsStore, Task> assertion)
    {
        var path = Path.Combine(Path.GetTempPath(), $"st-settings-{Guid.NewGuid():N}.db");
        try
        {
            await new LocalDatabase(path).InitializeAsync();
            await assertion(path, new AppSettingsStore(path));
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
}
