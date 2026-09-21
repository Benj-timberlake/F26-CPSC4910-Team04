using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace TruckerReward.Tests;

public sealed class ProductEnrichmentTests
{
    [Fact]
    public async Task MostWatchedFetchesMissingCountsAndKeepsUnknownLast()
    {
        using var handler = new DetailHandler();
        using var client = CreateClient(handler);
        var result = JsonNode.Parse(await client.SearchAsync("truck", default, "mostWatched"))!;
        Assert.Equal(new[] { "b", "a", "c", "d" }, Ids(result));
        Assert.Equal(3, handler.DetailRequests.Count);
        Assert.DoesNotContain("a", handler.DetailRequests);
        Assert.DoesNotContain("sort=mostWatched", handler.SearchUrl);
        Assert.NotNull(result["catalogNotice"]);
        Assert.Null(result["itemSummaries"]![3]!["watchCount"]);
    }

    [Fact]
    public async Task AvailabilitySortUsesDetailsWithUnavailableAndUnknownLast()
    {
        using var handler = new DetailHandler();
        using var client = CreateClient(handler);
        var result = JsonNode.Parse(await client.SearchAsync("truck", default, "availability"))!;
        Assert.Equal(new[] { "b", "c", "a", "d" }, Ids(result));
        Assert.Equal(4, handler.DetailRequests.Count);
        Assert.DoesNotContain("sort=availability", handler.SearchUrl);
        Assert.Equal("OUT_OF_STOCK", result["itemSummaries"]![2]!["availability"]!.GetValue<string>());
    }

    [Fact]
    public async Task InStockFilterCombinesWithMostWatchedAndOtherFilters()
    {
        using var handler = new DetailHandler();
        using var client = CreateClient(handler);
        var result = JsonNode.Parse(await client.SearchAsync("truck", default, "mostWatched",
            "maxDeliveryCost:0,returnsAccepted:true", availableOnly: true))!;
        Assert.Equal(new[] { "b", "c" }, Ids(result));
        Assert.Contains("filter=" + Uri.EscapeDataString("maxDeliveryCost:0,returnsAccepted:true"), handler.SearchUrl);
    }

    [Fact]
    public async Task InStockFilterPreservesServerPriceOrdering()
    {
        using var handler = new DetailHandler();
        using var client = CreateClient(handler);
        var result = JsonNode.Parse(await client.SearchAsync("truck", default, "-price", availableOnly: true))!;
        Assert.Equal(new[] { "b", "c" }, Ids(result));
        Assert.Contains("sort=-price", handler.SearchUrl);
    }

    [Fact]
    public async Task MissingIdsRemainUnknownWithoutDetailRequests()
    {
        using var handler = new DetailHandler { SearchBody = """{"itemSummaries":[{"title":"Missing ID"}]}""" };
        using var client = CreateClient(handler);
        var result = JsonNode.Parse(await client.SearchAsync("truck", default, "availability"))!;
        Assert.Single(result["itemSummaries"]!.AsArray());
        Assert.Empty(handler.DetailRequests);
        Assert.NotNull(result["catalogNotice"]);
    }

    [Fact]
    public async Task KnownWatchCountsSortNumericallyWithStableTiesWithoutExtraRequests()
    {
        using var handler = new DetailHandler { SearchBody = """
            {"itemSummaries":[{"itemId":"a","watchCount":2},{"itemId":"b","watchCount":10},{"itemId":"c","watchCount":10}]}
            """ };
        using var client = CreateClient(handler);
        var result = JsonNode.Parse(await client.SearchAsync("truck", default, "mostWatched"))!;
        Assert.Equal(new[] { "b", "c", "a" }, Ids(result));
        Assert.Empty(handler.DetailRequests);
        Assert.Null(result["catalogNotice"]);
    }

    [Theory]
    [InlineData("availability")]
    [InlineData("mostWatched")]
    public async Task EmptySearchSkipsEnrichment(string sort)
    {
        using var handler = new DetailHandler { SearchBody = """{"itemSummaries":[]}""" };
        using var client = CreateClient(handler);
        var result = JsonNode.Parse(await client.SearchAsync("truck", default, sort))!;
        Assert.Empty(Ids(result));
        Assert.Empty(handler.DetailRequests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("invalid JSON")]
    [InlineData("null")]
    public async Task UnknownOrMalformedAvailabilityIsExcludedFromInStock(string body)
    {
        using var handler = new DetailHandler { DetailBody = body };
        using var client = CreateClient(handler);
        var result = JsonNode.Parse(await client.SearchAsync("truck", default, availableOnly: true))!;
        Assert.Empty(Ids(result));
        Assert.NotNull(result["catalogNotice"]);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("timeout")]
    public async Task DetailFailuresPreserveSearchResultsAndReportMissingData(string failure)
    {
        using var handler = new DetailHandler { Failure = failure };
        using var client = CreateClient(handler);
        var result = JsonNode.Parse(await client.SearchAsync("truck", default, "availability"))!;
        Assert.Equal(new[] { "a", "b", "c", "d" }, Ids(result));
        Assert.NotNull(result["catalogNotice"]);
    }

    private static string[] Ids(JsonNode result) => result["itemSummaries"]!.AsArray()
        .Select(item => item!["itemId"]!.GetValue<string>()).ToArray();

    private static EbayClient CreateClient(DetailHandler handler) => new(new ClientFactory(handler),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Ebay:AccessToken"] = "test-token" }).Build());

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class DetailHandler : HttpMessageHandler
    {
        public string? DetailBody { get; init; }
        public string? Failure { get; init; }
        public string SearchBody { get; init; } = """
            {"itemSummaries":[{"itemId":"a","watchCount":5},{"itemId":"b"},{"itemId":"c"},{"itemId":"d"}]}
            """;
        public ConcurrentBag<string> DetailRequests { get; } = new();
        public string? SearchUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("Bearer test-token", request.Headers.Authorization!.ToString());
            Assert.Equal("EBAY_US", request.Headers.GetValues("X-EBAY-C-MARKETPLACE-ID").Single());
            if (request.RequestUri!.AbsolutePath.EndsWith("/search"))
            {
                SearchUrl = request.RequestUri.AbsoluteUri;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(SearchBody) });
            }
            var id = request.RequestUri.Segments.Last();
            DetailRequests.Add(id);
            if (Failure == "http") throw new HttpRequestException("Connection failed");
            if (Failure == "timeout") throw new TaskCanceledException("Timed out");
            var body = DetailBody ?? (id switch
            {
                "a" => """{"itemEndDate":"2020-01-01T00:00:00Z","estimatedAvailabilities":[{"estimatedAvailabilityStatus":"IN_STOCK"}]}""",
                "b" => """{"watchCount":20,"estimatedAvailabilities":[{"estimatedAvailabilityStatus":"IN_STOCK"}]}""",
                "c" => """{"watchCount":0,"estimatedAvailabilities":[{"estimatedAvailabilityStatus":"LIMITED_STOCK"}]}""",
                _ => "{}"
            });
            return Task.FromResult(new HttpResponseMessage(id == "d" ? HttpStatusCode.NotFound : HttpStatusCode.OK)
            { Content = new StringContent(body) });
        }
    }
}
