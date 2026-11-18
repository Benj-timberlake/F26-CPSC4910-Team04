using System.Net;
using System.Text.Json;
using FrontEnd;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace TruckerReward.Tests;

public sealed class DifyChatClientTests
{
    [Fact]
    public async Task SendsBlockingChatRequestWithKeyAndReturnsConversation()
    {
        var handler = new DifyHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"answer":"Check your dashboard.","conversation_id":"conversation-123"}""")
        });
        var client = CreateClient(handler, "https://ai.example.com", "secret-key");

        var reply = await client.SendAsync("Where are my points?", "browser-session", null);

        Assert.Equal("Check your dashboard.", reply.Answer);
        Assert.Equal("conversation-123", reply.ConversationId);
        Assert.Equal("https://ai.example.com/v1/chat-messages", handler.RequestUri?.ToString());
        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("secret-key", handler.AuthorizationParameter);
        using var body = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("Where are my points?", body.RootElement.GetProperty("query").GetString());
        Assert.Equal("browser-session", body.RootElement.GetProperty("user").GetString());
        Assert.Equal("blocking", body.RootElement.GetProperty("response_mode").GetString());
        Assert.Equal(JsonValueKind.Object, body.RootElement.GetProperty("inputs").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("conversation_id").ValueKind);
    }

    [Fact]
    public async Task RejectsInsecurePublicDifyUrlBeforeSendingApiKey()
    {
        var handler = new DifyHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var client = CreateClient(handler, "http://ai.example.com", "secret-key");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.SendAsync("Hello", "browser-session", null));

        Assert.Null(handler.RequestUri);
    }

    private static DifyChatClient CreateClient(DifyHandler handler, string url, string apiKey)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DIFY_API_URL"] = url,
                ["DIFY_API_KEY"] = apiKey
            })
            .Build();
        return new DifyChatClient(new HttpClient(handler), configuration);
    }

    private sealed class DifyHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}
