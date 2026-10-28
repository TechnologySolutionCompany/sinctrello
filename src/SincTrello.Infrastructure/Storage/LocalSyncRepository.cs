using System.Text.Json;
using Microsoft.Data.Sqlite;
using SincTrello.Core;

namespace SincTrello.Infrastructure.Storage;

public sealed class LocalSyncRepository(string databasePath)
{
    private SqliteConnection Open()
    { var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, ForeignKeys = true }.ToString()); c.Open(); return c; }
    public (string BoardId, string Path)? FindRoot(string rootId)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT BoardId, LastAbsolutePath FROM LocalSyncRoot WHERE RootId=$id";
        cmd.Parameters.AddWithValue("$id", rootId);
        using var reader = cmd.ExecuteReader(); return reader.Read() ? (reader.GetString(0), reader.GetString(1)) : null;
    }
    public bool IsRegisteredPath(string path)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM LocalSyncRoot WHERE LastAbsolutePath=$path COLLATE NOCASE";
        cmd.Parameters.AddWithValue("$path", path); return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }
    public string[] KnownCardIds(string rootId, string boardId)
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT CardId FROM LocalSyncCard WHERE RootId=$root AND BoardId=$board
            UNION SELECT o.CardId FROM SyncOperation o JOIN SyncRun r ON r.SyncId=o.SyncId
            WHERE r.RootId=$root AND r.BoardId=$board AND o.Status IN ('Applying','Failed')
            """;
        cmd.Parameters.AddWithValue("$root", rootId); cmd.Parameters.AddWithValue("$board", boardId);
        using var reader = cmd.ExecuteReader(); var ids = new List<string>(); while (reader.Read()) ids.Add(reader.GetString(0)); return ids.ToArray();
    }
    public string BeginRun(PreparedFolderSync prepared)
    {
        var id = Guid.NewGuid().ToString("N");
        using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO LocalSyncRoot VALUES ($root,$board,$path) ON CONFLICT(RootId) DO UPDATE SET LastAbsolutePath=excluded.LastAbsolutePath";
        cmd.Parameters.AddWithValue("$root", prepared.Marker.RootId); cmd.Parameters.AddWithValue("$board", prepared.Board.Id); cmd.Parameters.AddWithValue("$path", prepared.RootPath); cmd.ExecuteNonQuery();
        cmd.CommandText = "UPDATE LocalSyncCard SET RelativePath=NULL WHERE RootId=$root AND BoardId=$board"; cmd.ExecuteNonQuery();
        foreach (var card in prepared.Actual.Cards) UpsertCard(cmd, prepared.Marker.RootId, prepared.Board.Id, card.CardId, card.ActualDirectory);
        cmd.Parameters.Clear();
        cmd.CommandText = "INSERT INTO SyncRun (SyncId,StartedAt,Status,CardsRead,RootId,BoardId) VALUES ($id,$at,'Running',$count,$root,$board)";
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); cmd.Parameters.AddWithValue("$count", prepared.Desired.Cards.Count);
        cmd.Parameters.AddWithValue("$root", prepared.Marker.RootId); cmd.Parameters.AddWithValue("$board", prepared.Board.Id); cmd.ExecuteNonQuery();
        foreach (var op in prepared.Plan.Operations)
        {
            cmd.Parameters.Clear(); cmd.CommandText = "INSERT INTO SyncOperation (SyncId,CardId,Type,Source,Destination,Status) VALUES ($sync,$card,$type,$from,$to,'Planned')";
            cmd.Parameters.AddWithValue("$sync", id); cmd.Parameters.AddWithValue("$card", op.CardId); cmd.Parameters.AddWithValue("$type", op.Type.ToString());
            cmd.Parameters.AddWithValue("$from", (object?)op.Source ?? DBNull.Value); cmd.Parameters.AddWithValue("$to", op.Destination); cmd.ExecuteNonQuery();
        }
        tx.Commit(); return id;
    }
    public void MarkApplying(string syncId, string cardId)
    { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "UPDATE SyncOperation SET Status='Applying' WHERE SyncId=$sync AND CardId=$card"; cmd.Parameters.AddWithValue("$sync", syncId); cmd.Parameters.AddWithValue("$card", cardId); cmd.ExecuteNonQuery(); }
    public void CompleteOperation(string syncId, PreparedFolderSync prepared, SyncOperation op)
    {
        using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        UpsertCard(cmd, prepared.Marker.RootId, prepared.Board.Id, op.CardId, op.Destination);
        cmd.Parameters.Clear(); cmd.CommandText = "UPDATE SyncOperation SET Status='Completed' WHERE SyncId=$sync AND CardId=$card";
        cmd.Parameters.AddWithValue("$sync", syncId); cmd.Parameters.AddWithValue("$card", op.CardId); cmd.ExecuteNonQuery(); tx.Commit();
    }
    public void FinishRun(string syncId, string status, int created, int moved, int renamed, string message)
    {
        using var c = Open(); using var tx = c.BeginTransaction(); using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "UPDATE SyncOperation SET Status=CASE WHEN Status='Applying' THEN 'Failed' ELSE 'Skipped' END WHERE SyncId=$id AND Status IN ('Applying','Planned')";
        cmd.Parameters.AddWithValue("$id", syncId); cmd.ExecuteNonQuery();
        cmd.CommandText = "UPDATE SyncRun SET FinishedAt=$at,Status=$status,FoldersCreated=$created,FoldersMoved=$moved,FoldersRenamed=$renamed,Errors=$errors WHERE SyncId=$id";
        cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); cmd.Parameters.AddWithValue("$status", status); cmd.Parameters.AddWithValue("$created", created); cmd.Parameters.AddWithValue("$moved", moved); cmd.Parameters.AddWithValue("$renamed", renamed);
        cmd.Parameters.AddWithValue("$errors", JsonSerializer.Serialize(status == "Failed" ? new[] { message } : Array.Empty<string>())); cmd.ExecuteNonQuery(); tx.Commit();
    }
    public IReadOnlyList<SyncHistoryEntry> ReadHistory()
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT SyncId,Status,StartedAt,FoldersCreated,FoldersMoved,FoldersRenamed FROM SyncRun ORDER BY StartedAt DESC LIMIT 50";
        using var reader = cmd.ExecuteReader(); var list = new List<SyncHistoryEntry>();
        while (reader.Read())
        {
            var status = reader.GetString(1); var created = reader.GetInt32(3); var moved = reader.GetInt32(4); var renamed = reader.GetInt32(5);
            list.Add(new(reader.GetString(0), status, DateTimeOffset.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture), created, moved, renamed,
                $"{created} criadas · {moved} movidas · {renamed} renomeadas"));
        }
        return list;
    }
    private static void UpsertCard(SqliteCommand cmd, string root, string board, string card, string path)
    {
        cmd.Parameters.Clear(); cmd.CommandText = "INSERT INTO LocalSyncCard VALUES ($root,$board,$card,$path,$at) ON CONFLICT(RootId,BoardId,CardId) DO UPDATE SET RelativePath=excluded.RelativePath,UpdatedUtc=excluded.UpdatedUtc";
        cmd.Parameters.AddWithValue("$root", root); cmd.Parameters.AddWithValue("$board", board); cmd.Parameters.AddWithValue("$card", card); cmd.Parameters.AddWithValue("$path", path); cmd.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); cmd.ExecuteNonQuery();
    }
}
