using System.Security.Claims;

namespace FrontEnd.Auth;

// proxies the driver's own points csv for the browser
public static class PointsExport
{
    public static void MapPointsExport(this WebApplication app) =>
        app.MapGet("/points/export", async (HttpContext http, IHttpClientFactory clients) =>
        {
            var id = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            using var response = await clients.CreateClient("Backend").GetAsync($"users/{id}/points.csv");
            if (!response.IsSuccessStatusCode)
                return Results.StatusCode((int)response.StatusCode);
            return Results.File(await response.Content.ReadAsByteArrayAsync(), "text/csv", "points-history.csv");
        }).RequireAuthorization(policy => policy.RequireRole("driver"));
}
