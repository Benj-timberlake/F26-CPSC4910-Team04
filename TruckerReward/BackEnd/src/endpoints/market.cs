using System.Net.Http.Headers;

public static class MarketEndpoints
{
    public static void MapMarketEndpoints(this WebApplication app)
    {
        app.MapGet("/api/products", async Task<IResult> (
            string q,
            IHttpClientFactory httpClientFactory,
            IConfiguration config) =>
        {
            var token = config["Ebay:AccessToken"];

            if (string.IsNullOrWhiteSpace(token))
            {
                return Results.Problem(
                    "The eBay access token is not configured.");
            }

            var client = httpClientFactory.CreateClient();

            var url =
                "https://api.ebay.com/buy/browse/v1/item_summary/search" +
                $"?q={Uri.EscapeDataString(q)}&limit=10";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            request.Headers.Add(
                "X-EBAY-C-MARKETPLACE-ID", "EBAY_US");

            using var response = await client.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                return Results.Problem(
                    detail: $"eBay returned status {(int)response.StatusCode}.",
                    statusCode: 502);
            }

            var json = await response.Content.ReadAsStringAsync();

            return Results.Content(json, "application/json");
        });
    }
}