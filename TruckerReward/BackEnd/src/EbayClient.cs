using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

public sealed class EbayClient(IHttpClientFactory clients, IConfiguration configuration) : IDisposable
{
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? cachedToken;
    private DateTimeOffset expiresAt;

    public async Task<string?> GetItemAsync(string itemId, CancellationToken cancellationToken)
    {
        var origin = (configuration["Ebay:Environment"] ?? "Production").ToLowerInvariant() switch
        {
            "production" => "https://api.ebay.com",
            "sandbox" => "https://api.sandbox.ebay.com",
            _ => throw new InvalidOperationException("Invalid eBay environment.")
        };
        using var client = clients.CreateClient();
        var token = await GetTokenAsync(client, origin, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            origin + "/buy/browse/v1/item/" + Uri.EscapeDataString(itemId));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-EBAY-C-MARKETPLACE-ID", "EBAY_US");
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
            throw new EbayException($"eBay returned status {(int)response.StatusCode}.");
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("itemId", out _))
            throw new JsonException("Invalid item response.");
        return json;
    }

    public async Task<string> SearchAsync(string query, CancellationToken cancellationToken,
        string? sort = null, string? filter = null, bool availableOnly = false)
    {
        var environment = configuration["Ebay:Environment"] ?? "Production";
        var origin = environment.ToLowerInvariant() switch
        {
            "production" => "https://api.ebay.com",
            "sandbox" => "https://api.sandbox.ebay.com",
            _ => throw new InvalidOperationException("Invalid eBay environment.")
        };
        using var client = clients.CreateClient();
        var token = await GetTokenAsync(client, origin, cancellationToken);
        var localSort = sort is "availability" or "mostWatched";
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{origin}/buy/browse/v1/item_summary/search?q={Uri.EscapeDataString(query)}&limit=12" +
            (string.IsNullOrEmpty(sort) || localSort ? "" : "&sort=" + Uri.EscapeDataString(sort)) +
            (string.IsNullOrEmpty(filter) ? "" : "&filter=" + Uri.EscapeDataString(filter)));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-EBAY-C-MARKETPLACE-ID", "EBAY_US");
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new EbayException($"eBay returned status {(int)response.StatusCode}.");
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        if (localSort || availableOnly)
            return await EnrichProductsAsync(client, origin, token, json, sort, availableOnly, cancellationToken);
        return json;
    }

    private static async Task<string> EnrichProductsAsync(HttpClient client, string origin, string token,
        string json, string? sort, bool availableOnly, CancellationToken cancellationToken)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("Invalid search response.");
        if (root["itemSummaries"] is not JsonArray items || items.Count == 0) return json;
        var products = items.OfType<JsonObject>().ToArray();
        using var concurrency = new SemaphoreSlim(4);
        var incomplete = 0;
        await Task.WhenAll(products.Select(async product =>
        {
            // Watch counts already returned by search do not require another request unless
            // availability is also requested. Unknown counts must never become zero watches.
            var needsAvailability = sort == "availability" || availableOnly;
            if (!needsAvailability && product["watchCount"] is not null) return;
            var id = product["itemId"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(id))
            {
                Interlocked.Increment(ref incomplete);
                return;
            }
            await concurrency.WaitAsync(cancellationToken);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    origin + "/buy/browse/v1/item/" + Uri.EscapeDataString(id));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Add("X-EBAY-C-MARKETPLACE-ID", "EBAY_US");
                using var response = await client.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    Interlocked.Increment(ref incomplete);
                    return;
                }
                var detail = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken))
                    as JsonObject ?? throw new JsonException("Invalid item response.");
                if (detail["watchCount"] is { } count) product["watchCount"] = count.DeepClone();
                if (needsAvailability) product["availability"] = ReadAvailability(detail);
            }
            catch (HttpRequestException) { Interlocked.Increment(ref incomplete); }
            catch (JsonException) { Interlocked.Increment(ref incomplete); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { Interlocked.Increment(ref incomplete); }
            finally { concurrency.Release(); }
        }));

        IEnumerable<JsonObject> ordered = products;
        if (availableOnly) ordered = ordered.Where(p => p["availability"]?.GetValue<string>() is "IN_STOCK" or "LIMITED_STOCK");
        if (sort == "availability")
            ordered = ordered.OrderBy(p => p["availability"]?.GetValue<string>() switch
            {
                "IN_STOCK" => 0,
                "LIMITED_STOCK" => 1,
                "OUT_OF_STOCK" => 2,
                _ => 3
            });
        else if (sort == "mostWatched")
            ordered = ordered.OrderByDescending(p => p["watchCount"]?.GetValue<long>());
        var result = ordered.Select(p => p.DeepClone()).ToArray();
        root["itemSummaries"] = new JsonArray(result);
        var missingAvailability = (sort == "availability" || availableOnly) &&
            products.Any(p => p["availability"] is null);
        var missingWatches = sort == "mostWatched" && products.Any(p => p["watchCount"] is null);
        if (incomplete > 0 || missingAvailability || missingWatches)
            root["catalogNotice"] = "Some availability or watch counts could not be retrieved from eBay.";
        return root.ToJsonString();
    }

    private static string? ReadAvailability(JsonObject detail)
    {
        if (DateTimeOffset.TryParse(detail["itemEndDate"]?.GetValue<string>(), out var end) && end <= DateTimeOffset.UtcNow)
            return "OUT_OF_STOCK";
        var states = (detail["estimatedAvailabilities"] as JsonArray)?.OfType<JsonObject>()
            .Select(a => a["estimatedAvailabilityStatus"]?.GetValue<string>()).ToArray() ?? [];
        if (states.Contains("IN_STOCK")) return "IN_STOCK";
        if (states.Contains("LIMITED_STOCK")) return "LIMITED_STOCK";
        if (states.Length > 0 && states.All(s => s == "OUT_OF_STOCK")) return "OUT_OF_STOCK";
        return null;
    }

    private async Task<string> GetTokenAsync(HttpClient client, string origin, CancellationToken cancellationToken)
    {
        // An explicit token remains useful for local API testing.
        var suppliedToken = configuration["Ebay:AccessToken"];
        if (!string.IsNullOrWhiteSpace(suppliedToken)) return suppliedToken;
        var clientId = configuration["Ebay:ClientId"];
        var secret = configuration["Ebay:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException("Missing eBay credentials.");

        await tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (cachedToken is not null && DateTimeOffset.UtcNow < expiresAt) return cachedToken;
            using var request = new HttpRequestMessage(HttpMethod.Post, origin + "/identity/v1/oauth2/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{secret}")));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["scope"] = "https://api.ebay.com/oauth/api_scope"
            });
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new EbayException($"eBay token request returned status {(int)response.StatusCode}. Check your ClientId, ClientSecret, and Environment.");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            if (!root.TryGetProperty("access_token", out var accessToken) || accessToken.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(accessToken.GetString()) ||
                !root.TryGetProperty("expires_in", out var expiry) || !expiry.TryGetInt32(out var seconds) || seconds <= 0)
                throw new JsonException("Invalid eBay token response.");
            cachedToken = accessToken.GetString()!;
            expiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, seconds - 60));
            return cachedToken;
        }
        finally { tokenLock.Release(); }
    }

    public void Dispose() => tokenLock.Dispose();
}

public sealed class EbayException(string message) : Exception(message);
