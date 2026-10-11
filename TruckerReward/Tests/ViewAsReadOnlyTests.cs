using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FrontEnd.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class ViewAsReadOnlyTests
{
    private readonly JsonBackend backend = new();

    private static ClaimsPrincipal User(bool viewing) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, "8"), .. viewing ? new[] { new Claim(ViewAs.ViewerIdClaim, "4") } : []], "test"));

    private HttpClient Client(ClaimsPrincipal? request = null, ClaimsPrincipal? circuit = null)
    {
        var circuits = new CircuitServices
        {
            Services = circuit is null ? null : new ServiceCollection().AddSingleton<AuthenticationStateProvider>(new FixedAuth(circuit)).BuildServiceProvider()
        };
        var http = new HttpContextAccessor { HttpContext = request is null ? null : new DefaultHttpContext { User = request } };
        return new HttpClient(new ViewAsReadOnlyHandler(circuits, http) { InnerHandler = backend }) { BaseAddress = new Uri("http://backend/") };
    }

    [Fact]
    public async Task ViewingBlocksWritesBeforeTheyReachTheBackend()
    {
        backend.Routes["GET /users/8/points"] = (HttpStatusCode.OK, "{}");
        var client = Client(circuit: User(viewing: true));

        var write = await client.PostAsJsonAsync("users/8/cart/checkout", new { });
        var read = await client.GetAsync("users/8/points");

        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Equal(ViewAsReadOnlyHandler.Message, (await write.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(["GET /users/8/points"], backend.Requests);
    }

    [Fact]
    public async Task PlainRequestsAreCheckedToo()
    {
        var client = Client(request: User(viewing: true));

        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync("users/8/passkeys/3")).StatusCode);
        Assert.Empty(backend.Requests);
    }

    [Theory]
    [InlineData(false, "users/8/cart/checkout")]
    [InlineData(true, "auth/login")]
    public async Task OtherWritesGoThrough(bool viewing, string path)
    {
        backend.Routes[$"POST /{path}"] = (HttpStatusCode.OK, "{}");
        var client = Client(request: User(viewing), circuit: User(viewing));

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(path, new { })).StatusCode);
    }

    private sealed class FixedAuth(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(user));
    }
}
