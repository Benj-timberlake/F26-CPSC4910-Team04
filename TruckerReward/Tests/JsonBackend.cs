using System.Net;
using System.Text;

namespace TruckerReward.Tests;

// fake backend for page tests, keyed by "METHOD /path?query"
public sealed class JsonBackend : HttpMessageHandler
{
    public Dictionary<string, (HttpStatusCode Status, string Json)> Routes { get; } = [];
    public Dictionary<string, string> Bodies { get; } = [];
    public List<string> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = $"{request.Method} {request.RequestUri!.PathAndQuery}";
        Requests.Add(key);
        if (request.Content is not null)
            Bodies[key] = await request.Content.ReadAsStringAsync(cancellationToken);
        var (status, json) = Routes.TryGetValue(key, out var route) ? route : (HttpStatusCode.NotFound, "");
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
