using Microsoft.Data.Sqlite;

namespace SincTrello.Infrastructure;

public sealed class LocalDatabase(string databasePath)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString());
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS SchemaMigrations (Version INTEGER PRIMARY KEY);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        command.CommandText = "SELECT COUNT(*) FROM SchemaMigrations WHERE Version = 1";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 0)
        {
            command.CommandText = """
                CREATE TABLE AppSettings (Id INTEGER PRIMARY KEY CHECK (Id = 1), Json TEXT NOT NULL);
                CREATE TABLE Card (
                  TrelloCardId TEXT PRIMARY KEY, TrelloShortLink TEXT, BoardId TEXT NOT NULL,
                  ListId TEXT NOT NULL, CardName TEXT NOT NULL, CardUrl TEXT NOT NULL,
                  MemberIds TEXT NOT NULL, CurrentLocalPath TEXT, LastKnownLocalPath TEXT,
                  LastSyncUtc TEXT, CreatedLocalUtc TEXT NOT NULL, UpdatedLocalUtc TEXT NOT NULL);
                CREATE TABLE SyncRun (
                  SyncId TEXT PRIMARY KEY, StartedAt TEXT NOT NULL, FinishedAt TEXT,
                  Status TEXT NOT NULL, CardsRead INTEGER NOT NULL DEFAULT 0,
                  FoldersCreated INTEGER NOT NULL DEFAULT 0, FoldersMoved INTEGER NOT NULL DEFAULT 0,
                  FoldersRenamed INTEGER NOT NULL DEFAULT 0, AttachmentsDownloaded INTEGER NOT NULL DEFAULT 0,
                  Warnings TEXT NOT NULL DEFAULT '[]', Errors TEXT NOT NULL DEFAULT '[]');
                CREATE TABLE SyncOperation (
                  Id INTEGER PRIMARY KEY, SyncId TEXT NOT NULL REFERENCES SyncRun(SyncId),
                  CardId TEXT NOT NULL, Type TEXT NOT NULL, Source TEXT, Destination TEXT NOT NULL,
                  Status TEXT NOT NULL);
                INSERT INTO SchemaMigrations VALUES (1);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        command.CommandText = "SELECT COUNT(*) FROM SchemaMigrations WHERE Version = 2";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 0)
        {
            command.CommandText = """
                CREATE TABLE LocalSyncRoot (RootId TEXT PRIMARY KEY, BoardId TEXT NOT NULL, LastAbsolutePath TEXT NOT NULL);
                CREATE TABLE LocalSyncCard (
                  RootId TEXT NOT NULL, BoardId TEXT NOT NULL, CardId TEXT NOT NULL,
                  RelativePath TEXT COLLATE NOCASE, UpdatedUtc TEXT NOT NULL,
                  PRIMARY KEY (RootId, BoardId, CardId), UNIQUE (RootId, BoardId, RelativePath));
                ALTER TABLE SyncRun ADD COLUMN RootId TEXT;
                ALTER TABLE SyncRun ADD COLUMN BoardId TEXT;
                INSERT INTO SchemaMigrations VALUES (2);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        transaction.Commit();
    }
}
