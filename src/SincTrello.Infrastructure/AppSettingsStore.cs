using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SincTrello.Infrastructure;

/// <summary>Persists preferences only. Credentials must use the Windows credential store.</summary>
public sealed class AppSettingsStore(string databasePath)
{
    private SqliteConnection CreateConnection() => new(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Json FROM AppSettings WHERE Id = 1";
        var json = await command.ExecuteScalarAsync(cancellationToken) as string;
        return json is null ? new AppSettings() : JsonSerializer.Deserialize<AppSettings>(json)
            ?? throw new JsonException("Configuração local inválida.");
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO AppSettings (Id, Json) VALUES (1, $json) ON CONFLICT(Id) DO UPDATE SET Json = excluded.Json";
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(settings));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
