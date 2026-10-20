using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FrontEnd.Auth;

public static class DriverViewEndpoints
{
    public static void MapDriverViewEndpoints(this WebApplication app)
    {
        app.MapPost("/account/driver-view/{driverId:int}", async (
            int driverId, HttpContext http, IHttpClientFactory clients) =>
        {
            if (!http.User.IsInRole("sponsor") || http.User.HasClaim(c => c.Type == TruckerSignIn.SponsorViewClaim) || ViewAs.IsViewing(http.User))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var sponsorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            using var client = clients.CreateClient("Backend");
            using var response = await client.GetAsync($"users/{sponsorId}/drivers/{driverId}/view");
            if (!response.IsSuccessStatusCode)
                return Results.Problem("You can only view active drivers associated with your company.", statusCode: 403);
            var driver = await response.Content.ReadFromJsonAsync<UserProfile>();
            if (driver is null || driver.UserType != "driver" || driver.Id != driverId)
                return Results.Problem("Couldn't load the driver.", statusCode: 502);
            await TruckerSignIn.SignInAsync(http, driver, sponsorId);
            return Results.LocalRedirect("/dashboard");
        }).RequireAuthorization().DisableAntiforgery();

        app.MapPost("/account/driver-view/return", async (
            HttpContext http, IHttpClientFactory clients) =>
        {
            var sponsorId = http.User.FindFirstValue(TruckerSignIn.SponsorViewClaim);
            if (sponsorId is null)
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            using var client = clients.CreateClient("Backend");
            using var status = await client.GetAsync($"users/{sponsorId}/status");
            if (!status.IsSuccessStatusCode ||
                (await status.Content.ReadFromJsonAsync<AccountStatus>())?.Status != "active")
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            using var response = await client.GetAsync($"users/{sponsorId}");
            if (!response.IsSuccessStatusCode)
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            var sponsor = await response.Content.ReadFromJsonAsync<UserProfile>();
            if (sponsor is null || sponsor.UserType != "sponsor" || sponsor.Id.ToString() != sponsorId)
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            // Explicitly replace the driver identity, including the origin claim.
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            http.User = new ClaimsPrincipal(new ClaimsIdentity());
            await TruckerSignIn.SignInAsync(http, sponsor);
            return Results.LocalRedirect("/dashboard");
        }).RequireAuthorization().DisableAntiforgery();
    }

    private sealed record AccountStatus(string Status);
}
