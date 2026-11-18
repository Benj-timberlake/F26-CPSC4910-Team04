using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FrontEnd;

public sealed class DifyChatClient(HttpClient httpClient, IConfiguration configuration)
{
    public async Task<DifyChatReply> SendAsync(
        string query,
        string userId,
        string? conversationId,
        CancellationToken cancellationToken = default)
    {
        var apiUrl = configuration["DIFY_API_URL"];
        if (!Uri.TryCreate(apiUrl?.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps
                && !(baseUri.Scheme == Uri.UriSchemeHttp && baseUri.IsLoopback)))
        {
            throw new InvalidOperationException("DIFY_API_URL must be an HTTPS URL (HTTP is allowed for localhost).");
        }

        var apiKey = configuration["DIFY_API_KEY"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("DIFY_API_KEY is not configured.");

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "v1/chat-messages"))
        {
            Content = JsonContent.Create(new DifyChatRequest(query, userId, conversationId))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<DifyChatResponse>(cancellationToken);
        if (string.IsNullOrWhiteSpace(result?.Answer))
            throw new JsonException("Dify returned an empty chat answer.");

        return new DifyChatReply(result.Answer, result.ConversationId);
    }

    private sealed record DifyChatRequest(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("user")] string User,
        [property: JsonPropertyName("conversation_id")] string? ConversationId)
    {
        [JsonPropertyName("inputs")]
        public Dictionary<string, string> Inputs { get; } = [];

        [JsonPropertyName("response_mode")]
        public string ResponseMode { get; } = "blocking";
    }

    private sealed record DifyChatResponse
    {
        [JsonPropertyName("answer")]
        public string? Answer { get; init; }

        [JsonPropertyName("conversation_id")]
        public string? ConversationId { get; init; }
    }
}

public sealed record DifyChatReply(string Answer, string? ConversationId);
