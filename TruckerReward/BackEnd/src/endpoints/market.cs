using System.Text.Json;

public static class MarketEndpoints
{
    public static void MapMarketEndpoints(this WebApplication app)
    {
        app.MapGet("/api/products/detail", async Task<IResult> (
            string itemId, EbayClient ebay, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(itemId)) return Results.BadRequest();
            try
            {
                var json = await ebay.GetItemAsync(itemId.Trim(), cancellationToken);
                return json is null ? Results.NotFound() : Results.Content(json, "application/json");
            }
            catch (InvalidOperationException)
            { return Results.Problem("Check the backend eBay configuration."); }
            catch (EbayException)
            { return Results.Problem("Could not load the eBay product.", statusCode: 502); }
            catch (HttpRequestException)
            { return Results.Problem("Could not connect to eBay.", statusCode: 502); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { return Results.Problem("eBay took too long to respond.", statusCode: 504); }
            catch (JsonException)
            { return Results.Problem("eBay returned an invalid product.", statusCode: 502); }
        });
        app.MapGet("/api/products", async Task<IResult> (
            string q, EbayClient ebay, CancellationToken cancellationToken,
            string? sort = null, bool freeShipping = false, bool returnsAccepted = false, bool availableOnly = false) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.BadRequest(new { detail = "Enter a product search." });
            if (sort is not (null or "" or "price" or "-price" or "newlyListed" or "availability" or "mostWatched"))
                return Results.BadRequest(new { detail = "Unsupported sort option." });
            var filters = new List<string>();
            if (freeShipping) filters.Add("maxDeliveryCost:0");
            if (returnsAccepted) filters.Add("returnsAccepted:true");
            try
            {
                var json = await ebay.SearchAsync(q.Trim(), cancellationToken, sort, string.Join(',', filters), availableOnly);
                return Results.Content(json, "application/json");
            }
            catch (InvalidOperationException)
            {
                return Results.Problem("Check the backend eBay App ID, Cert ID, and environment configuration.");
            }
            catch (EbayException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 502);
            }
            catch (HttpRequestException)
            {
                return Results.Problem(detail: "Could not connect to eBay. Try again later.", statusCode: 502);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Results.Problem(detail: "eBay took too long to respond. Try again.", statusCode: 504);
            }
            catch (JsonException)
            {
                return Results.Problem(detail: "eBay returned an invalid response.", statusCode: 502);
            }
        });
    }
}
