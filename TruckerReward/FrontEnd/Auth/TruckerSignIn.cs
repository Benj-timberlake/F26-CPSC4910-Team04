using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FrontEnd.Auth;

// turns the backend's user profile into the auth cookie. used at login and again by
// CookieRevalidation when it refreshes the cookie from the current user row
public static class TruckerSignIn
{
    public const string SponsorViewClaim = "sponsor_view_id";
    public const string SessionVersionClaim = "session_version";
    // this browser's row on the devices page
    public const string SessionKeyClaim = "session_key";

    public static ClaimsPrincipal DriverPrincipal(UserProfile driver, string sponsorId)
    {
        var principal = Principal(driver);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(SponsorViewClaim, sponsorId));
        return principal;
    }

    public static ClaimsPrincipal Principal(UserProfile user) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Email, user.Email),
        new Claim(ClaimTypes.Role, user.UserType),
        new Claim(SessionVersionClaim, user.SessionVersion.ToString())
    ], CookieAuthenticationDefaults.AuthenticationScheme));

    public static async Task SignInAsync(HttpContext http, UserProfile user, string? sponsorId = null)
    {
        var clock = http.RequestServices.GetRequiredService<TimeProvider>();
        var properties = new AuthenticationProperties();
        CookieRevalidation.MarkChecked(properties, clock.GetUtcNow());
        sponsorId ??= http.User.FindFirstValue(SponsorViewClaim);
        var driverView = sponsorId is not null && user.UserType == "driver";
        var principal = driverView ? DriverPrincipal(user, sponsorId!) : Principal(user);
        // a refreshed cookie or a sponsor's driver view is still the same browser
        var key = http.User.FindFirstValue(SessionKeyClaim) ?? (driverView ? null : await StartSession(http, user));
        if (key is not null)
            ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(SessionKeyClaim, key));
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, properties);
    }

    // signing in still works if this fails, the browser just won't be listed
    private static async Task<string?> StartSession(HttpContext http, UserProfile user)
    {
        try
        {
            var backend = http.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("Backend");
            using var response = await backend.PostAsJsonAsync($"users/{user.Id}/sessions", new
            {
                ipAddress = http.Connection.RemoteIpAddress?.ToString(),
                userAgent = http.Request.Headers.UserAgent.ToString()
            });
            return response.IsSuccessStatusCode ? (await response.Content.ReadFromJsonAsync<StartedSession>())?.Key : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private sealed record StartedSession(string Key);
}
