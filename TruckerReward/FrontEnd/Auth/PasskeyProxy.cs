using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

namespace FrontEnd.Auth;

// proxies passkeys.js calls to set the cookie
public static class PasskeyProxy
{
    public static void MapPasskeyProxy(this WebApplication app)
    {
        app.MapPost("/account/passkeys/options", async (HttpContext http, IHttpClientFactory clients) =>
            await Forward(await clients.CreateClient("Backend").PostAsync($"users/{Id(http)}/passkeys/begin", null)))
            .RequireAuthorization().AddEndpointFilter(ViewAs.NotWhileViewing).DisableAntiforgery();

        app.MapPost("/account/passkeys", async (NewPasskey body, HttpContext http, IHttpClientFactory clients) =>
            await Forward(await clients.CreateClient("Backend").PostAsJsonAsync($"users/{Id(http)}/passkeys/complete", body)))
            .RequireAuthorization().AddEndpointFilter(ViewAs.NotWhileViewing).DisableAntiforgery();

        app.MapPost("/account/passkey-login/options", async (IHttpClientFactory clients) =>
            await Forward(await clients.CreateClient("Backend").PostAsync("auth/passkey/begin", null)))
            .DisableAntiforgery();

        app.MapPost("/account/passkey-login", async (PasskeyLogin body, HttpContext http, IHttpClientFactory clients) =>
        {
            using var response = await clients.CreateClient("Backend").PostAsJsonAsync("auth/passkey/complete",
                new { body.RequestId, body.Response, ipAddress = http.Connection.RemoteIpAddress?.ToString() });
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return Results.Json(new { message = "This account has been deactivated. Contact an administrator." }, statusCode: StatusCodes.Status403Forbidden);
            if (!response.IsSuccessStatusCode)
                return Results.Json(new { message = "That passkey didn't work. Try again or use your password." }, statusCode: StatusCodes.Status401Unauthorized);
            await TruckerSignIn.SignInAsync(http, (await response.Content.ReadFromJsonAsync<UserProfile>())!);
            var returnUrl = body.ReturnUrl;
            return Results.Ok(new { redirect = !string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") ? returnUrl : "/dashboard" });
        }).DisableAntiforgery();
    }

    private static string? Id(HttpContext http) => http.User.FindFirstValue(ClaimTypes.NameIdentifier);

    private static async Task<IResult> Forward(HttpResponseMessage response)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            return Results.Text(body == "" ? "{}" : body, "application/json", statusCode: (int)response.StatusCode);
        }
    }

    public sealed record NewPasskey(string? RequestId, string? Name, JsonElement Response);
    public sealed record PasskeyLogin(string? RequestId, string? ReturnUrl, JsonElement Response);
}
