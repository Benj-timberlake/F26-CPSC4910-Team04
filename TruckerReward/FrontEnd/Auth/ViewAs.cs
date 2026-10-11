using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FrontEnd.Auth;

// swaps the cookie to another user for a fixed half hour, keeping who is really signed in
public static class ViewAs
{
    public const string ViewerIdClaim = "viewer_id";
    public const string ViewerNameClaim = "viewer_name";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private const string UntilKey = "view_as_until";

    public static void MapViewAs(this WebApplication app)
    {
        app.MapPost("/account/view-as/{userId:int}", async (int userId, HttpContext http, IHttpClientFactory clients, TimeProvider clock) =>
        {
            if (IsViewing(http.User))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var viewerId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            using var response = await clients.CreateClient("Backend").PostAsJsonAsync($"users/{viewerId}/view-as/{userId}",
                new { ipAddress = http.Connection.RemoteIpAddress?.ToString() });
            if (!response.IsSuccessStatusCode)
                return Results.StatusCode((int)response.StatusCode);
            var user = (await response.Content.ReadFromJsonAsync<UserProfile>())!;

            var principal = TruckerSignIn.Principal(user);
            ((ClaimsIdentity)principal.Identity!).AddClaims([
                new Claim(ViewerIdClaim, viewerId!),
                new Claim(ViewerNameClaim, http.User.Identity!.Name!)
            ]);
            var now = clock.GetUtcNow();
            var properties = new AuthenticationProperties { ExpiresUtc = now + Lifetime };
            properties.Items[UntilKey] = (now + Lifetime).ToString("o");
            CookieRevalidation.MarkChecked(properties, now);
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
            return Results.Redirect("/dashboard");
        }).RequireAuthorization(policy => policy.RequireRole("admin", "sponsor")).DisableAntiforgery();

        app.MapPost("/account/view-as/stop", async (HttpContext http, IHttpClientFactory clients) =>
        {
            var viewerId = http.User.FindFirstValue(ViewerIdClaim);
            if (viewerId is null)
                return Results.Redirect("/dashboard");
            var backend = clients.CreateClient("Backend");
            using var status = await backend.GetAsync($"users/{viewerId}/status");
            using var response = await backend.GetAsync($"users/{viewerId}");
            var viewer = response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<UserProfile>() : null;
            if (viewer is null || !status.IsSuccessStatusCode || (await status.Content.ReadFromJsonAsync<Status>())?.Value != "active")
            {
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.Redirect("/login");
            }
            await TruckerSignIn.SignInAsync(http, viewer);
            return Results.Redirect(viewer.UserType == "admin" ? "/admin/accounts" : "/manage-points");
        }).RequireAuthorization().DisableAntiforgery();
    }

    public static bool IsViewing(ClaimsPrincipal user) => user.HasClaim(c => c.Type == ViewerIdClaim);

    // the viewer can't change the account's sign-in details
    public static async ValueTask<object?> NotWhileViewing(EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        IsViewing(context.HttpContext.User) ? Results.StatusCode(StatusCodes.Status403Forbidden) : await next(context);

    internal static bool Expired(AuthenticationProperties properties, DateTimeOffset now) =>
        !properties.Items.TryGetValue(UntilKey, out var s)
        || !DateTimeOffset.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var until)
        || now >= until;

    // keeps the viewer claims when the cookie is rebuilt from the backend
    internal static ClaimsPrincipal Carry(ClaimsPrincipal from, ClaimsPrincipal to)
    {
        ((ClaimsIdentity)to.Identity!).AddClaims(from.Claims.Where(c => c.Type is ViewerIdClaim or ViewerNameClaim).Select(c => new Claim(c.Type, c.Value)));
        return to;
    }

    private sealed record Status([property: System.Text.Json.Serialization.JsonPropertyName("status")] string Value);
}
