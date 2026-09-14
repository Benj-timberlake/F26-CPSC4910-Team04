using System.Text.Json;

public static class MarketEndpoints
{
    public static void MapMarketEndpoints(this WebApplication app)
    {
        app.MapGet("/api/products", async Task<IResult> (
            string q, EbayClient ebay, CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.BadRequest(new { detail = "Enter a product search." });
            try
            {
                var json = await ebay.SearchAsync(q.Trim(), cancellationToken);
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
