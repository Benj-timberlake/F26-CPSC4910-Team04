using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace FrontEnd.Auth;

// plain form posts from the login/register pages. blazor components can't set cookies once the
// circuit is up, so the sign in has to happen in a normal request
public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        app.MapPost("/account/login", async (
            [FromForm] string username,
            [FromForm] string password,
            [FromForm] string? returnUrl,
            HttpContext http,
            IHttpClientFactory clients) =>
        {
            var backend = clients.CreateClient("Backend");
            using var response = await backend.PostAsJsonAsync("auth/login", new
            {
                username,
                password,
                ipAddress = http.Connection.RemoteIpAddress?.ToString()
            });

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return Results.Redirect("/login?error=1" + ReturnParam(returnUrl));
            if (response.StatusCode == HttpStatusCode.Locked)
                return Results.Redirect("/login?error=locked" + ReturnParam(returnUrl));
            response.EnsureSuccessStatusCode();

            var user = await response.Content.ReadFromJsonAsync<UserProfile>();
            await TruckerSignIn.SignInAsync(http, user!);
            return Results.LocalRedirect(SafeReturn(returnUrl));
        }).DisableAntiforgery();

        app.MapPost("/account/register", async (
            [FromForm] string username,
            [FromForm] string email,
            [FromForm] string password,
            [FromForm] string userType,
            [FromForm] string? companyName,
            [FromForm] string? phoneNumber,
            [FromForm] string? address,
            HttpContext http,
            IHttpClientFactory clients) =>
        {
            var backend = clients.CreateClient("Backend");
            using var response = await backend.PostAsJsonAsync("auth/register", new
            {
                username, email, password, userType, companyName, phoneNumber, address
            });

            if (!response.IsSuccessStatusCode)
                return Results.Redirect("/register?error=" + await ErrorParam(response, "Could not create the account."));

            var user = await response.Content.ReadFromJsonAsync<UserProfile>();
            await TruckerSignIn.SignInAsync(http, user!);
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

            var backend = clients.CreateClient("Backend");
            using var response = await backend.PostAsJsonAsync("auth/external", new
            {
                provider,
                email,
                name,
                ipAddress = http.Connection.RemoteIpAddress?.ToString()
            });
            if (!response.IsSuccessStatusCode)
                return Results.Redirect("/login?error=sso");

            var user = await response.Content.ReadFromJsonAsync<UserProfile>();
            await http.SignOutAsync(AuthenticationSetup.ExternalScheme);
            await TruckerSignIn.SignInAsync(http, user!);
            return Results.LocalRedirect(SafeReturn(returnUrl));
        });

        app.MapPost("/account/forgot", async ([FromForm] string email, HttpContext http, IHttpClientFactory clients) =>
        {
            using var response = await clients.CreateClient("Backend").PostAsJsonAsync("auth/forgot", new
            {
                email,
                ipAddress = http.Connection.RemoteIpAddress?.ToString()
            });
            // the backend answers the same way for unknown emails, so the page always says "sent"
            return Results.Redirect(response.IsSuccessStatusCode ? "/forgot-password?sent=1" : "/forgot-password?error=1");
        }).DisableAntiforgery();

        app.MapPost("/account/reset", async ([FromForm] string token, [FromForm] string password, HttpContext http, IHttpClientFactory clients) =>
        {
            using var response = await clients.CreateClient("Backend").PostAsJsonAsync("auth/reset", new
            {
                token,
                newPassword = password,
                ipAddress = http.Connection.RemoteIpAddress?.ToString()
            });
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
            using var response = await clients.CreateClient("Backend").PostAsJsonAsync($"users/{id}/password", new
            {
                currentPassword,
                newPassword = password,
                ipAddress = http.Connection.RemoteIpAddress?.ToString()
            });
            if (!response.IsSuccessStatusCode)
                return Results.Redirect("/account/password?error=" + await ErrorParam(response, "Could not change the password."));
            return Results.Redirect("/dashboard?password=changed");
        }).RequireAuthorization().DisableAntiforgery();

        app.MapPost("/account/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/");
        }).DisableAntiforgery();
    }

    private static string SafeReturn(string? returnUrl) =>
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
public record UserProfile(int Id, string UserType, string Username, string Email, string PhoneNumber, string Address);
