using System.Net;
using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using FrontEnd.Pages;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace TruckerReward.Tests;

public sealed class PasskeysPageTests : TestContext
{
    private readonly JsonBackend backend = new();
    private const string One = """[{"id":3,"name":"iPhone","createdAt":"2026-10-01T08:00:00","lastUsedAt":null}]""";

    private IRenderedComponent<Passkeys> Render(params Claim[] extra)
    {
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("bob");
        auth.SetClaims([new Claim(ClaimTypes.NameIdentifier, "8"), .. extra]);
        Services.AddHttpClient("Backend", c => c.BaseAddress = new Uri("http://backend/"))
            .ConfigurePrimaryHttpMessageHandler(() => backend);
        return RenderComponent<Passkeys>();
    }

    [Fact]
    public void AddingRunsTheBrowserCeremonyAndReloads()
    {
        backend.Routes["GET /users/8/passkeys"] = (HttpStatusCode.OK, "[]");
        JSInterop.Setup<string?>("truckerPasskeys.register", "Work phone").SetResult(null);
        var page = Render();
        page.WaitForAssertion(() => Assert.Contains("No passkeys yet.", page.Markup));

        backend.Routes["GET /users/8/passkeys"] = (HttpStatusCode.OK, One);
        page.Find("#passkey-name").Change(" Work phone ");
        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Contains("iPhone", page.Find(".passkey-list").TextContent));
        Assert.Contains("Passkey added.", page.Find(".tr-alert-success").TextContent);
    }

    [Fact]
    public void BrowserProblemsAreShown()
    {
        backend.Routes["GET /users/8/passkeys"] = (HttpStatusCode.OK, "[]");
        JSInterop.Setup<string?>("truckerPasskeys.register", "This device").SetResult("This browser or address can't use passkeys.");
        var page = Render();
        page.WaitForAssertion(() => page.Find("form"));

        page.Find("form").Submit();

        page.WaitForAssertion(() => Assert.Equal("This browser or address can't use passkeys.", page.Find(".tr-alert-error").TextContent));
    }

    [Fact]
    public void RemovingDeletesAndReloads()
    {
        backend.Routes["GET /users/8/passkeys"] = (HttpStatusCode.OK, One);
        backend.Routes["DELETE /users/8/passkeys/3"] = (HttpStatusCode.NoContent, "");
        var page = Render();
        page.WaitForAssertion(() => page.Find(".passkey-list button"));

        backend.Routes["GET /users/8/passkeys"] = (HttpStatusCode.OK, "[]");
        page.Find(".passkey-list button").Click();

        page.WaitForAssertion(() => Assert.Equal("iPhone was removed.", page.Find(".tr-alert-success").TextContent));
        Assert.Contains("DELETE /users/8/passkeys/3", backend.Requests);
    }

    [Fact]
    public void NothingChangesWhileViewingAsSomeone()
    {
        backend.Routes["GET /users/8/passkeys"] = (HttpStatusCode.OK, One);
        var page = Render(new Claim(FrontEnd.Auth.ViewAs.ViewerIdClaim, "4"));

        page.WaitForAssertion(() => page.Find(".passkey-list"));
        Assert.Empty(page.FindAll("form"));
        Assert.Empty(page.FindAll("button"));
    }

    [Fact]
    public void LoginPageOffersPasskeysWithTheReturnUrl()
    {
        Services.AddSingleton<IAuthenticationSchemeProvider, NoSchemes>();
        Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>().NavigateTo("login?returnUrl=%2Fpoints%22x");

        var login = RenderComponent<Login>();

        var button = login.Find(".passkey button");
        Assert.Equal("/points\"x", button.GetAttribute("data-return-url"));
        Assert.StartsWith("truckerPasskeys.signIn(this.dataset.returnUrl", button.GetAttribute("onclick"));
    }

    private sealed class NoSchemes : IAuthenticationSchemeProvider
    {
        public Task<IEnumerable<AuthenticationScheme>> GetAllSchemesAsync() => Task.FromResult(Enumerable.Empty<AuthenticationScheme>());
        public Task<AuthenticationScheme?> GetSchemeAsync(string name) => Task.FromResult<AuthenticationScheme?>(null);
        public Task<AuthenticationScheme?> GetDefaultAuthenticateSchemeAsync() => Task.FromResult<AuthenticationScheme?>(null);
        public Task<AuthenticationScheme?> GetDefaultChallengeSchemeAsync() => Task.FromResult<AuthenticationScheme?>(null);
        public Task<AuthenticationScheme?> GetDefaultForbidSchemeAsync() => Task.FromResult<AuthenticationScheme?>(null);
        public Task<AuthenticationScheme?> GetDefaultSignInSchemeAsync() => Task.FromResult<AuthenticationScheme?>(null);
        public Task<AuthenticationScheme?> GetDefaultSignOutSchemeAsync() => Task.FromResult<AuthenticationScheme?>(null);
        public void AddScheme(AuthenticationScheme scheme) { }
        public void RemoveScheme(string name) { }
        public Task<IEnumerable<AuthenticationScheme>> GetRequestHandlerSchemesAsync() => Task.FromResult(Enumerable.Empty<AuthenticationScheme>());
    }
}
