using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SincTrello.Infrastructure.Trello;
using Xunit;

namespace SincTrello.Infrastructure.Tests;

public sealed class TrelloReadServiceTests
{
    private const string Board = "600000000000000000000001", List = "600000000000000000000002", ArchivedList = "600000000000000000000003", Member = "600000000000000000000004";
    private static readonly TrelloCredentials Credentials = new("test-api-key", "test-token-never-in-urls");

    [Fact]
    public async Task ReadsCompleteSnapshotWithoutPerCardCallsAndWithoutWritingOrLeakingCredentials()
    {
        using var handler = new Handler((request, _) => Task.FromResult(Route(request, [Card(10), Card(11, list: ArchivedList)])));
        using var http = new HttpClient(handler);
        var result = await new TrelloReadService(http, Credentials).GetBoardAsync(Board, default);
        Assert.Equal(Board, result.Id);
        Assert.Single(result.Lists);
        var card = Assert.Single(result.Cards);
        Assert.Equal(Member, Assert.Single(card.MemberIds));
        Assert.Equal(123L, Assert.Single(card.Attachments).Size);
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("api.trello.com", request.Uri.Host);
            Assert.DoesNotContain(Credentials.Token, request.Uri.AbsoluteUri);
            Assert.DoesNotContain(Credentials.ApiKey, request.Uri.AbsoluteUri);
            Assert.Contains(Credentials.Token, request.Authorization);
        });
        Assert.Null(http.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task PaginatesCardsWithStableCursorAndRejectsRepeatedPages()
    {
        var page = Enumerable.Range(1, 1000).Reverse().Select(i => Card(i + 100)).ToArray();
        var cardCalls = 0;
        using var handler = new Handler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/cards", StringComparison.Ordinal))
            {
                cardCalls++;
                if (cardCalls == 1) return Task.FromResult(Ok(page));
                Assert.Contains("before=" + Id(101), request.RequestUri.Query);
                return Task.FromResult(Ok(new[] { Card(100) }));
            }
            return Task.FromResult(Route(request, []));
        });
        using var http = new HttpClient(handler);
        var snapshot = await new TrelloReadService(http, Credentials).GetBoardAsync(Board, default);
        Assert.Equal(1001, snapshot.Cards.Count);
        Assert.Equal(2, cardCalls);

        using var repeated = new Handler((request, _) => Task.FromResult(Route(request, page)));
        using var repeatedHttp = new HttpClient(repeated);
        await Assert.ThrowsAsync<TrelloApiException>(() => new TrelloReadService(repeatedHttp, Credentials).GetBoardAsync(Board, default));
        Assert.Equal(4, repeated.Requests.Count);
    }

    [Theory]
    [InlineData(401)] [InlineData(403)] [InlineData(404)] [InlineData(429)]
    public async Task AuthenticationAndAccessErrorsAreExplicitAndRedacted(int status)
    {
        using var handler = new Handler((_, _) =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(Credentials.Token) };
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<TrelloApiException>(() => new TrelloReadService(http, Credentials).GetCurrentMemberAsync(default));
        Assert.Equal((HttpStatusCode)status, ex.StatusCode);
        Assert.DoesNotContain(Credentials.Token, ex.ToString());
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("null")] [InlineData("{}")] [InlineData("not-json")]
    [InlineData("{\"id\":\"600000000000000000000004\",\"username\":null,\"fullName\":\"Name\"}")]
    public async Task InvalidPayloadIsNeverReportedAsAnEmptySuccess(string body)
    {
        using var handler = new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) }));
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<TrelloApiException>(() => new TrelloReadService(http, Credentials).GetCurrentMemberAsync(default));
    }

    [Fact]
    public async Task RetriesTransientReadsWithBoundedAttempts()
    {
        var count = 0;
        using var handler = new Handler((_, _) =>
        {
            count++;
            if (count == 3) return Task.FromResult(Ok(new { id = Member, username = "mine", fullName = "My Name" }));
            var response = new HttpResponseMessage(count == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        Assert.Equal(Member, (await new TrelloReadService(http, Credentials).GetCurrentMemberAsync(default)).Id);
        Assert.Equal(3, count);
    }

    [Fact]
    public async Task CancellationReachesHttpAndNetworkExceptionsDoNotLeakSecrets()
    {
        using var cancellation = new CancellationTokenSource();
        using var waiting = new Handler(async (_, token) => { cancellation.Cancel(); await Task.Delay(Timeout.Infinite, token); return Ok(new { }); });
        using var waitingHttp = new HttpClient(waiting);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new TrelloReadService(waitingHttp, Credentials).GetCurrentMemberAsync(cancellation.Token));
        using var broken = new Handler((_, _) => throw new HttpRequestException(Credentials.Token));
        using var brokenHttp = new HttpClient(broken);
        var ex = await Assert.ThrowsAsync<TrelloApiException>(() => new TrelloReadService(brokenHttp, Credentials).GetCurrentMemberAsync(default));
        Assert.DoesNotContain(Credentials.Token, ex.ToString());
    }

    [Fact]
    public async Task RejectsDuplicateIdentitiesAndCardsFromOtherBoards()
    {
        foreach (var cards in new[] { new[] { Card(10), Card(10) }, new[] { Card(10, board: "600000000000000000000009") } })
        {
            using var handler = new Handler((request, _) => Task.FromResult(Route(request, cards)));
            using var http = new HttpClient(handler);
            await Assert.ThrowsAsync<TrelloApiException>(() => new TrelloReadService(http, Credentials).GetBoardAsync(Board, default));
        }
    }

    [Theory]
    [InlineData("https://trello.com/b/aBcD1234/quadro", "aBcD1234")]
    [InlineData(Board, Board)]
    public void BoardReferencesAreNormalized(string input, string expected) => Assert.Equal(expected, TrelloReadService.ParseBoardReference(input));
    [Theory]
    [InlineData("https://evil.test/b/aBcD1234")]
    [InlineData("../members/me")]
    [InlineData("aBcD1234?token=secret")]
    public void BoardReferencesCannotInjectHostsOrPaths(string input) => Assert.Throws<ArgumentException>(() => TrelloReadService.ParseBoardReference(input));

    private static string Id(int value) => "70000000" + value.ToString("x16");
    private static object Card(int index, string list = List, string board = Board) => new
    {
        id = Id(index), idBoard = board, idList = list, name = "SSC 1000101 - Cliente", url = "https://trello.com/c/aBcD1234/card", shortLink = "aBcD1234", desc = "Técnico: My Name\nPrioridade: Média", idMembers = new[] { Member }, closed = false,
        attachments = new[] { new { id = "600000000000000000000005", name = "relatorio.sql", url = "https://trello.com/1/cards/attachments/sql", bytes = 123L } }
    };
    private static HttpResponseMessage Route(HttpRequestMessage request, object[] cards) => request.RequestUri!.AbsolutePath switch
    {
        "/1/boards/" + Board => Ok(new { id = Board, name = "Demandas", closed = false }),
        "/1/boards/" + Board + "/lists" => Ok(new[] { new { id = List, name = "Em Andamento", closed = false, pos = 1 }, new { id = ArchivedList, name = "Arquivo", closed = true, pos = 2 } }),
        "/1/boards/" + Board + "/cards" => Ok(cards),
        _ => throw new InvalidOperationException("Unexpected read endpoint")
    };
    private static HttpResponseMessage Ok(object data) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri, string Authorization)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!, request.Headers.Authorization?.ToString() ?? ""));
            return respond(request, cancellationToken);
        }
    }
}
