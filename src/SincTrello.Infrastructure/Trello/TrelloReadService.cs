using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using SincTrello.Core;

namespace SincTrello.Infrastructure.Trello;

/// <summary>Only GET requests; request credentials are never placed in URLs or shared default headers.</summary>
public sealed class TrelloReadService(HttpClient httpClient, TrelloCredentials credentials) : ITrelloReadClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Uri ApiRoot = new("https://api.trello.com/1/");
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(35);

    public Task<MemberSnapshot> GetCurrentMemberAsync(CancellationToken cancellationToken) => ResolveMemberAsync("me", cancellationToken);

    public async Task<MemberSnapshot> ResolveMemberAsync(string username, CancellationToken cancellationToken)
    {
        if (!Regex.IsMatch(username, @"\A[a-zA-Z0-9_-]{1,128}\z")) throw new ArgumentException("Usuário Trello inválido.");
        var dto = await GetAsync<MemberDto>($"members/{Uri.EscapeDataString(username)}?fields=id,username,fullName", cancellationToken);
        RequireId(dto.Id); RequireText(dto.Username); RequireText(dto.FullName);
        return new(dto.Id, dto.Username, dto.FullName);
    }

    public async Task<IReadOnlyList<TrelloBoardChoice>> GetBoardsAsync(CancellationToken cancellationToken)
    {
        var boards = await GetAsync<BoardDto[]>("members/me/boards?filter=open&fields=id,name,url,closed", cancellationToken);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var board in boards)
        {
            RequireId(board.Id); RequireText(board.Name);
            if (!ids.Add(board.Id)) throw InvalidResponse();
        }
        return boards.Where(b => !b.Closed).OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(b => new TrelloBoardChoice(b.Id, b.Name, b.Url ?? "")).ToArray();
    }

    public async Task<BoardSnapshot> GetBoardAsync(string boardId, CancellationToken cancellationToken)
    {
        var reference = ParseBoardReference(boardId);
        var board = await GetAsync<BoardDto>($"boards/{reference}?fields=id,name,closed", cancellationToken);
        RequireId(board.Id); RequireText(board.Name);
        if (reference.Length == 24 && !string.Equals(reference, board.Id, StringComparison.OrdinalIgnoreCase)) throw InvalidResponse();
        if (board.Closed) throw new TrelloApiException("O quadro está arquivado. Escolha um quadro aberto.");
        var lists = await GetAsync<ListDto[]>($"boards/{board.Id}/lists?filter=all&fields=id,name,closed,pos", cancellationToken);
        var byId = new Dictionary<string, ListDto>(StringComparer.Ordinal);
        foreach (var list in lists)
        {
            RequireId(list.Id); RequireText(list.Name);
            if (!byId.TryAdd(list.Id, list)) throw InvalidResponse();
        }
        var cards = new List<CardSnapshot>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? before = null;
        for (var page = 0; page < 100; page++)
        {
            var url = $"boards/{board.Id}/cards?filter=open&limit=1000&sort=-id&fields=id,idBoard,idList,name,url,shortLink,desc,idMembers,closed&attachments=true&attachment_fields=id,name,url,bytes";
            if (before is not null) url += "&before=" + before;
            var batch = await GetAsync<CardDto[]>(url, cancellationToken);
            foreach (var card in batch)
            {
                RequireId(card.Id); RequireId(card.IdList); RequireId(card.IdBoard); RequireText(card.Name); RequireText(card.Url);
                if (!seen.Add(card.Id) || card.IdBoard != board.Id || !byId.TryGetValue(card.IdList, out var list) ||
                    card.IdMembers is null || card.Attachments is null || card.Desc is null ||
                    (before is not null && string.CompareOrdinal(card.Id, before) >= 0)) throw InvalidResponse();
                foreach (var memberId in card.IdMembers) RequireId(memberId);
                var attachmentIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var attachment in card.Attachments)
                {
                    RequireId(attachment.Id); RequireText(attachment.Name); RequireText(attachment.Url);
                    if (!attachmentIds.Add(attachment.Id) || attachment.Bytes < 0) throw InvalidResponse();
                }
                if (!card.Closed && !list.Closed)
                    cards.Add(new(card.Id, board.Id, card.IdList, card.Name, card.Url, card.ShortLink, card.Desc,
                        card.IdMembers, card.Attachments.Select(a => new AttachmentSnapshot(a.Id, a.Name, a.Url, a.Bytes)).ToArray()));
            }
            if (batch.Length < 1000)
                return new BoardSnapshot(board.Id, board.Name, lists.Where(l => !l.Closed).OrderBy(l => l.Pos).Select(l => new ListSnapshot(l.Id, l.Name)).ToArray(), cards);
            before = batch.Select(c => c.Id).Min(StringComparer.Ordinal);
        }
        throw new TrelloApiException("A leitura excedeu o limite de páginas. Nenhum resultado parcial foi aplicado.");
    }

    public static string ParseBoardReference(string value)
    {
        var reference = value.Trim();
        if (Uri.TryCreate(reference, UriKind.Absolute, out var uri))
        {
            var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (uri.Scheme != "https" || uri.Host != "trello.com" || segments.Length < 2 || segments[0] != "b")
                throw new ArgumentException("Informe o link https://trello.com/b/... ou o ID do quadro.");
            reference = segments[1];
        }
        if (!Regex.IsMatch(reference, @"\A(?:[a-zA-Z0-9]{8}|[a-fA-F0-9]{24})\z"))
            throw new ArgumentException("Informe um ID de quadro ou um link do Trello válido.");
        return reference;
    }

    private async Task<T> GetAsync<T>(string relativeUri, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(ApiRoot, relativeUri));
                request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", $"oauth_consumer_key=\"{credentials.ApiKey}\", oauth_token=\"{credentials.Token}\"");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var transient = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout or HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
                    var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromMilliseconds(350 * (attempt + 1));
                    if (transient && attempt < 2 && retry <= TimeSpan.FromSeconds(10))
                    {
                        await Task.Delay(retry < TimeSpan.Zero ? TimeSpan.Zero : retry, timeout.Token).ConfigureAwait(false);
                        continue;
                    }
                    throw new TrelloApiException(response.StatusCode switch
                    {
                        HttpStatusCode.Unauthorized => "A autorização expirou ou foi revogada. Conecte sua conta novamente.",
                        HttpStatusCode.Forbidden => "O Trello negou acesso. Confira a autorização e sua participação no quadro.",
                        HttpStatusCode.NotFound => "Quadro ou usuário não encontrado, ou sem acesso para esta conta.",
                        HttpStatusCode.TooManyRequests => "O Trello limitou as consultas. Aguarde e tente novamente.",
                        _ => "O Trello não concluiu a consulta. Tente novamente em instantes."
                    }, response.StatusCode);
                }
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                return await JsonSerializer.DeserializeAsync<T>(stream, Json, timeout.Token).ConfigureAwait(false) ?? throw InvalidResponse();
            }
        }
        catch (JsonException) { throw InvalidResponse(); }
        catch (HttpRequestException) { throw new TrelloApiException("Não foi possível acessar o Trello. Confira sua conexão com a internet."); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TrelloApiException("O Trello demorou para responder. Tente novamente."); }
    }

    private static void RequireId(string? value) { if (value is null || !Regex.IsMatch(value, @"\A[a-fA-F0-9]{24}\z")) throw InvalidResponse(); }
    private static void RequireText(string? value) { if (value is null) throw InvalidResponse(); }
    private static TrelloApiException InvalidResponse() => new("O Trello retornou dados incompletos ou inconsistentes. Atualize novamente; os dados anteriores foram preservados.");

    private sealed class BoardDto
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required bool Closed { get; init; }
        public string? Url { get; init; }
    }
    private sealed class MemberDto
    {
        public required string Id { get; init; }
        public required string Username { get; init; }
        public required string FullName { get; init; }
    }
    private sealed class ListDto
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required bool Closed { get; init; }
        public double Pos { get; init; }
    }
    private sealed class CardDto
    {
        public required string Id { get; init; }
        public required string IdBoard { get; init; }
        public required string IdList { get; init; }
        public required string Name { get; init; }
        public required string Url { get; init; }
        public string? ShortLink { get; init; }
        public required string Desc { get; init; }
        public required string[] IdMembers { get; init; }
        public required AttachmentDto[] Attachments { get; init; }
        public required bool Closed { get; init; }
    }
    private sealed class AttachmentDto
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Url { get; init; }
        public long? Bytes { get; init; }
    }
}
