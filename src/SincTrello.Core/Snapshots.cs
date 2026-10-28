namespace SincTrello.Core;

public sealed record MemberSnapshot(string Id, string Username, string DisplayName);
public sealed record AttachmentSnapshot(string Id, string Name, string Url, long? Size);
public sealed record CardSnapshot(string Id, string BoardId, string ListId, string Name, string Url,
    string? ShortLink, string Description, IReadOnlyList<string> MemberIds, IReadOnlyList<AttachmentSnapshot> Attachments);
public sealed record ListSnapshot(string Id, string Name);
public sealed record BoardSnapshot(string Id, string Name, IReadOnlyList<ListSnapshot> Lists, IReadOnlyList<CardSnapshot> Cards);
public interface ITrelloService
{
    Task<BoardSnapshot> GetBoardAsync(string boardId, CancellationToken cancellationToken);
    Task<MemberSnapshot> ResolveMemberAsync(string username, CancellationToken cancellationToken);
}
public interface ICardOwnershipFilter { bool Includes(CardSnapshot card); }
public sealed class AssignedMemberCardFilter(string memberId) : ICardOwnershipFilter
{
    public bool Includes(CardSnapshot card) => card.MemberIds.Contains(memberId, StringComparer.Ordinal);
}
