using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace FrontEnd.Auth;

// plain form posts from the login/register pages. blazor components can't set cookies once the
// circuit is up, so the sign in has to happen in a normal request
public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        // where password managers look for the change-password page
        app.MapGet("/.well-known/change-password", () => Results.Redirect("/account/password"));

        app.MapPost("/account/login", async (
            [FromForm] string username,
            [FromForm] string password,
            [FromForm] string? returnUrl,
            HttpContext http,
            IHttpClientFactory clients) =>
        {
            using var response = await clients.CreateClient("Backend").PostAsJsonAsync("auth/login", new { username, password, ipAddress = Ip(http) });

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return Results.Redirect("/login?error=1" + ReturnParam(returnUrl));
            if (response.StatusCode == HttpStatusCode.Locked)
                return Results.Redirect("/login?error=locked" + ReturnParam(returnUrl));
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return Results.Redirect("/login?error=inactive" + ReturnParam(returnUrl));
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return Results.Redirect("/login?error=codes" + ReturnParam(returnUrl));
            if (response.StatusCode == HttpStatusCode.Accepted)
            {
                var pending = await response.Content.ReadFromJsonAsync<PendingCode>();
                PendingSignIn(http, pending!.Challenge, returnUrl);
                return Results.Redirect("/login/code");
            }
            if (!response.IsSuccessStatusCode)
                return Results.Redirect("/login?error=backend" + ReturnParam(returnUrl));

            await SignIn(http, response);
            return Results.LocalRedirect(SafeReturn(returnUrl));
        }).DisableAntiforgery();

        app.MapPost("/account/login/code", async ([FromForm] string code, HttpContext http, IHttpClientFactory clients) =>
        {
            if (PendingSignIn(http) is not var (challenge, returnUrl))
                return Results.Redirect("/login?error=code");
            using var response = await clients.CreateClient("Backend").PostAsJsonAsync("auth/login/code", new { challenge, code, ipAddress = Ip(http) });

            if (response.StatusCode == HttpStatusCode.BadRequest)
                return Results.Redirect("/login/code?error=wrong");
            http.Response.Cookies.Delete(PendingCookie);
            if (response.StatusCode == HttpStatusCode.Gone)
                return Results.Redirect("/login?error=code" + ReturnParam(returnUrl));
            if (response.StatusCode == HttpStatusCode.Locked)
                return Results.Redirect("/login?error=locked" + ReturnParam(returnUrl));
            if (!response.IsSuccessStatusCode)
                return Results.Redirect("/login?error=backend" + ReturnParam(returnUrl));

            await SignIn(http, response);
            return Results.LocalRedirect(SafeReturn(returnUrl));
        }).DisableAntiforgery();

        app.MapPost("/account/register", async (
            [FromForm] string username,
            [FromForm] string firstName,
            [FromForm] string lastName,
            [FromForm] string email,
            [FromForm] string password,
            [FromForm] string userType,
            [FromForm] string? phoneNumber,
            [FromForm] string? address,
            [FromForm] int? companyId,
            IFormFile? cdlLicense,
            HttpContext http,
            [FromServices] CdlStorage cdlStorage,
            IHttpClientFactory clients) =>
        {
            using var license = userType == "driver" && cdlLicense is not null
                ? await ReadLicense(cdlLicense, http.RequestAborted) : null;
            if (userType == "driver" && cdlLicense is not null && license is null)
                return Results.Redirect("/register?error=" + Uri.EscapeDataString("Choose a PDF, JPEG, or PNG CDL license, up to 10 MB."));

            using var response = await clients.CreateClient("Backend").PostAsJsonAsync("auth/register",
                new { username, firstName, lastName, email, password, userType, phoneNumber, address, companyId });

            if (!response.IsSuccessStatusCode)
                return Results.Redirect("/register?error=" + await ErrorParam(response, "Could not create the account."));

            var user = await response.Content.ReadFromJsonAsync<UserProfile>();
            await TruckerSignIn.SignInAsync(http, user!);
            if (license is not null && user!.UserType == "driver")
            {
                try { await cdlStorage.UploadAsync(user.Id, license, http.RequestAborted); }
                catch (Exception ex)
                {
                    http.RequestServices.GetRequiredService<ILogger<CdlStorage>>().LogError(ex, "Signup CDL upload failed for driver {DriverId}", user.Id);
                    return Results.LocalRedirect("/dashboard?cdlUpload=failed");
                }
            }
            return Results.LocalRedirect("/dashboard");
        }).DisableAntiforgery();

        app.MapGet("/account/external/{provider}", async (string provider, string? returnUrl, IAuthenticationSchemeProvider schemes) =>
        {
            if (await schemes.GetSchemeAsync(provider) is null)
                return Results.NotFound();
            return Results.Challenge(
                new AuthenticationProperties { RedirectUri = "/account/external-callback" + ReturnParam(returnUrl, first: true) },
                [provider]);
        });

        app.MapGet("/account/external-callback", async (string? returnUrl, HttpContext http, IHttpClientFactory clients) =>
        {
            var external = await http.AuthenticateAsync(AuthenticationSetup.ExternalScheme);
            if (!external.Succeeded)
                return Results.Redirect("/login?error=sso");

            var email = external.Principal.FindFirstValue(ClaimTypes.Email);
            var name = external.Principal.FindFirstValue(ClaimTypes.Name);
            var provider = external.Principal.Identity?.AuthenticationType;

            using var response = await clients.CreateClient("Backend").PostAsJsonAsync("auth/external", new { provider, email, name, ipAddress = Ip(http) });
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                await http.SignOutAsync(AuthenticationSetup.ExternalScheme);
                return Results.Redirect("/login?error=inactive");
            }
            if (!response.IsSuccessStatusCode)
                return Results.Redirect("/login?error=sso");

            var user = await response.Content.ReadFromJsonAsync<UserProfile>();
            await http.SignOutAsync(AuthenticationSetup.ExternalScheme);
            await TruckerSignIn.SignInAsync(http, user!);
            return Results.LocalRedirect(SafeReturn(returnUrl));
        });

        app.MapPost("/account/forgot", async ([FromForm] string email, HttpContext http, IHttpClientFactory clients) =>
        {
            using var response = await clients.CreateClient("Backend").PostAsJsonAsync("auth/forgot", new { email, ipAddress = Ip(http) });
            // the backend answers the same way for unknown emails, so the page always says "sent"
            return Results.Redirect(response.IsSuccessStatusCode ? "/forgot-password?sent=1" : "/forgot-password?error=1");
        }).DisableAntiforgery();

        app.MapPost("/account/reset", async ([FromForm] string token, [FromForm] string password, HttpContext http, IHttpClientFactory clients) =>
        {
            using var response = await clients.CreateClient("Backend").PostAsJsonAsync("auth/reset", new { token, newPassword = password, ipAddress = Ip(http) });
            if (!response.IsSuccessStatusCode)
                return Results.Redirect($"/reset-password?token={Uri.EscapeDataString(token)}&error=" + await ErrorParam(response, "Could not reset the password."));
            return Results.Redirect("/login?reset=1");
        }).DisableAntiforgery();

        app.MapPost("/account/change-password", async (
            [FromForm] string? currentPassword,
            [FromForm] string password,
            HttpContext http,
            IHttpClientFactory clients) =>
        {
            var id = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var sessionKey = http.User.FindFirstValue(TruckerSignIn.SessionKeyClaim);
            using var response = await clients.CreateClient("Backend").PostAsJsonAsync($"users/{id}/password",
                new { currentPassword, newPassword = password, ipAddress = Ip(http), sessionKey });
            if (!response.IsSuccessStatusCode)
                return Results.Redirect("/account/password?error=" + await ErrorParam(response, "Could not change the password."));
            // other browsers are signed out now, this one gets a cookie with the new version
            var user = await response.Content.ReadFromJsonAsync<UserProfile>();
            await TruckerSignIn.SignInAsync(http, user!);
            return Results.Redirect("/profile?password=changed");
        }).RequireAuthorization().AddEndpointFilter(ViewAs.NotWhileViewing).DisableAntiforgery();

        app.MapPost("/account/change-username", async (
            [FromForm] string? currentPassword,
            [FromForm] string username,
            HttpContext http,
            IHttpClientFactory clients) =>
        {
            var id = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            using var response = await clients.CreateClient("Backend").PostAsJsonAsync($"users/{id}/username",
                new { currentPassword, newUsername = username });
            if (!response.IsSuccessStatusCode)
                return Results.Redirect("/account/username?error=" + await ErrorParam(response, "Could not change the username."));
            // refresh the cookie so the new name shows right away
            var user = await response.Content.ReadFromJsonAsync<UserProfile>();
            await TruckerSignIn.SignInAsync(http, user!);
            return Results.Redirect("/profile?username=changed");
        }).RequireAuthorization().AddEndpointFilter(ViewAs.NotWhileViewing).DisableAntiforgery();

        app.MapPost("/account/change-email", async (
            [FromForm] string? currentPassword,
            [FromForm] string email,
            HttpContext http,
            IHttpClientFactory clients) =>
        {
            var id = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            using var response = await clients.CreateClient("Backend").PostAsJsonAsync($"users/{id}/email",
                new { currentPassword, newEmail = email });
            if (!response.IsSuccessStatusCode)
                return Results.Redirect("/account/email?error=" + await ErrorParam(response, "Could not change the email address."));
            var user = await response.Content.ReadFromJsonAsync<UserProfile>();
            await TruckerSignIn.SignInAsync(http, user!);
            return Results.Redirect("/profile?email=changed");
        }).RequireAuthorization().AddEndpointFilter(ViewAs.NotWhileViewing).DisableAntiforgery();

        app.MapPost("/account/update-profile", async (
            [FromForm] string firstName,
            [FromForm] string lastName,
            [FromForm] string? phoneNumber,
            [FromForm] string? address,
            HttpContext http,
            IHttpClientFactory clients) =>
        {
            var id = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            using var response = await clients.CreateClient("Backend").PutAsJsonAsync($"users/{id}/profile",
                new { firstName, lastName, phoneNumber, address });
            if (!response.IsSuccessStatusCode)
                return Results.Redirect("/account/profile?error=" + await ErrorParam(response, "Could not update your account."));
            return Results.Redirect("/profile?profile=updated");
        }).RequireAuthorization().AddEndpointFilter(ViewAs.NotWhileViewing).DisableAntiforgery();

        app.MapPost("/account/logout", async (HttpContext http, IHttpClientFactory clients) =>
        {
            if (http.User.FindFirstValue(TruckerSignIn.SessionKeyClaim) is { } key)
            {
                try
                {
                    using var _ = await clients.CreateClient("Backend").PostAsJsonAsync("sessions/end", new { key });
                }
                catch (HttpRequestException)
                {
                    // the cookie still goes, the row just ages out
                }
            }
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/");
        }).DisableAntiforgery();

        app.MapPost("/account/logout-everywhere", async (HttpContext http, IHttpClientFactory clients) =>
        {
            var id = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var response = await clients.CreateClient("Backend").PostAsJsonAsync($"users/{id}/sessions/revoke", new { actorId = int.Parse(id!) });
            if (!response.IsSuccessStatusCode)
                return Results.Redirect("/profile");
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login?signedOut=everywhere");
        }).RequireAuthorization().DisableAntiforgery();

        // the password, or the username for passwordless accounts
        app.MapPost("/account/delete", async ([FromForm] string? confirmation, HttpContext http, IHttpClientFactory clients) =>
        {
            var id = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"users/{id}/account") { Content = JsonContent.Create(new { confirmation }) };
            using var response = await clients.CreateClient("Backend").SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return Results.Redirect("/account/data?error=" + await ErrorParam(response, "Could not delete the account."));
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login?deleted=1");
        }).RequireAuthorization().AddEndpointFilter(ViewAs.NotWhileViewing).DisableAntiforgery();
    }

    // admins also get a cookie so the dashboard shows the admin image once
    internal static async Task SignIn(HttpContext http, HttpResponseMessage response)
    {
        var user = (await response.Content.ReadFromJsonAsync<UserProfile>())!;
        await TruckerSignIn.SignInAsync(http, user);
        if (!string.Equals(user.UserType, "admin", StringComparison.OrdinalIgnoreCase))
            return;
        http.Response.Cookies.Append("adminDance", "1", new CookieOptions
        {
            Path = "/dashboard",
            MaxAge = TimeSpan.FromSeconds(30),
            HttpOnly = false,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Lax
        });
    }

    private const string PendingCookie = "tr_code";

    // the challenge between the password and code steps, encrypted so it can't be read or forged
    private static void PendingSignIn(HttpContext http, string challenge, string? returnUrl)
    {
        var protector = Protector(http);
        http.Response.Cookies.Append(PendingCookie, protector.Protect(challenge + "\n" + returnUrl, TimeSpan.FromMinutes(10)), new CookieOptions
        {
            Path = "/",
            MaxAge = TimeSpan.FromMinutes(10),
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Lax
        });
    }

    private static (string Challenge, string? ReturnUrl)? PendingSignIn(HttpContext http)
    {
        if (!http.Request.Cookies.TryGetValue(PendingCookie, out var value))
            return null;
        try
        {
            var parts = Protector(http).Unprotect(value).Split('\n', 2);
            return (parts[0], parts.Length > 1 && parts[1] != "" ? parts[1] : null);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    private static ITimeLimitedDataProtector Protector(HttpContext http) =>
        http.RequestServices.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("sign-in-code").ToTimeLimitedDataProtector();

    private static async Task<MemoryStream?> ReadLicense(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length > CdlStorage.MaxBytes) return null;
        try
        {
            await using var source = file.OpenReadStream();
            return await CdlStorage.ReadAsync(source, cancellationToken);
        }
        catch (InvalidDataException) { return null; }
    }

    private static string? Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();

    internal static string SafeReturn(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") ? returnUrl : "/dashboard";

    private static string ReturnParam(string? returnUrl, bool first = false) =>
        string.IsNullOrEmpty(returnUrl) ? "" : (first ? "?" : "&") + "returnUrl=" + Uri.EscapeDataString(returnUrl);

    // the backend's { message } as a query value, or the fallback when the body isn't one
    private static async Task<string> ErrorParam(HttpResponseMessage response, string fallback)
    {
        ErrorBody? body = null;
        try { body = await response.Content.ReadFromJsonAsync<ErrorBody>(); } catch (System.Text.Json.JsonException) { }
        return Uri.EscapeDataString(body?.Message ?? fallback);
    }

    private sealed record ErrorBody(string? Message);
}

// shape of the backend's login/register response
public record PendingCode(string Challenge);

public record UserProfile(int Id, string UserType, string Username, string Email, string PhoneNumber, string Address, int SessionVersion = 0);
