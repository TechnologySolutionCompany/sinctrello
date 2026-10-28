using Microsoft.Data.Sqlite;
using SincTrello.Infrastructure;
using Xunit;

namespace SincTrello.Infrastructure.Tests;

public class DatabaseTests
{
    [Fact]
    public async Task MigrationIsIdempotentAndPreservesData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"st-{Guid.NewGuid():N}.db");
        try
        {
            var database = new LocalDatabase(path);
            await database.InitializeAsync();
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO AppSettings VALUES (1, '{}')";
                await command.ExecuteNonQueryAsync();
            }
            await database.InitializeAsync();
            using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                await connection.OpenAsync();
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM AppSettings";
                Assert.Equal(1L, await command.ExecuteScalarAsync());
                command.CommandText = "SELECT COUNT(*) FROM SchemaMigrations";
                Assert.Equal(2L, await command.ExecuteScalarAsync());
            }
        }
        finally { SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
}
