using SincTrello.Core;

namespace SincTrello.Infrastructure.Storage;

public sealed record SyncRootMarker(int Version, string RootId, string BoardId);
public sealed record CardFolderMetadata(int Version, string RootId, string BoardId, string CardId,
    string ListId, string CardName, string CardUrl, DateTimeOffset UpdatedUtc);
public sealed record SyncHistoryEntry(string SyncId, string Status, DateTimeOffset StartedAt, int Created, int Moved, int Renamed, string Summary);
public sealed record SyncExecutionProgress(int Completed, int Total, string Destination);
public sealed record SyncExecutionResult(string Status, int Completed, string Message);
public sealed class SyncValidationException(string message) : Exception(message);

public sealed class PreparedFolderSync
{
    internal PreparedFolderSync(string rootPath, SyncRootMarker marker, BoardSnapshot board, string memberId,
        DesiredState desired, ActualState actual, SyncPlan plan)
    { RootPath = rootPath; Marker = marker; Board = board; MemberId = memberId; Desired = desired; Actual = actual; Plan = plan; }
    public string RootPath { get; }
    public SyncRootMarker Marker { get; }
    public BoardSnapshot Board { get; }
    public string MemberId { get; }
    public DesiredState Desired { get; }
    public ActualState Actual { get; }
    public SyncPlan Plan { get; }
}
