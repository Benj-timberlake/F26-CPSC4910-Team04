using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public sealed class EbayClient(IHttpClientFactory clients, IConfiguration configuration) : IDisposable
{
    private readonly SemaphoreSlim tokenLock = new(1, 1);
    private string? cachedToken;
    private DateTimeOffset expiresAt;

    public async Task<string> SearchAsync(string query, CancellationToken cancellationToken)
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
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{origin}/buy/browse/v1/item_summary/search?q={Uri.EscapeDataString(query)}&limit=10");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("X-EBAY-C-MARKETPLACE-ID", "EBAY_US");
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new EbayException($"eBay returned status {(int)response.StatusCode}.");
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        return json;
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
