using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FrontEnd.Auth;

// The cookie carries the user's type as a role claim. Without this, an admin changing someone's
// user_type (or deleting the account) wouldn't take effect until the cookie expired, 8 hours
// later. So every few minutes the cookie is checked against the backend's current user row.
public sealed class CookieRevalidation(IHttpClientFactory clients, TimeProvider clock, ILogger<CookieRevalidation> log)
    : CookieAuthenticationEvents
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    private const string CheckedAtKey = "checked_at";

    public static void MarkChecked(AuthenticationProperties properties, DateTimeOffset at) =>
        properties.Items[CheckedAtKey] = at.ToString("o");

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var now = clock.GetUtcNow();
        if (now - CheckedAt(context.Properties) < Interval)
            return;

        var id = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id is null)
        {
            await Reject(context);
            return;
        }

        UserProfile? user;
        try
        {
            using var response = await clients.CreateClient("Backend").GetAsync($"users/{id}");
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                log.LogInformation("user {Id} no longer exists, signing out", id);
                await Reject(context);
                return;
            }
            // anything else that isn't a clean answer: keep the cookie and try again next request
            if (!response.IsSuccessStatusCode)
                return;
            user = await response.Content.ReadFromJsonAsync<UserProfile>();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "could not re-check user {Id} against the backend, keeping the cookie for now", id);
            return;
        }
        if (user is null)
            return;

        // username, email or type may have changed, rebuild the claims from the row
        context.ReplacePrincipal(TruckerSignIn.Principal(user));
        MarkChecked(context.Properties, now);
        context.ShouldRenew = true;
    }

    private static DateTimeOffset CheckedAt(AuthenticationProperties properties) =>
        properties.Items.TryGetValue(CheckedAtKey, out var s)
            && DateTimeOffset.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)
            ? t
            : properties.IssuedUtc ?? DateTimeOffset.MinValue;

    private static async Task Reject(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
