using System.Security.Claims;

namespace FrontEnd.Auth;

// proxies the signed-in user's own data download for the browser
public static class DataExport
{
    public static void MapDataExport(this WebApplication app) =>
        app.MapGet("/account/data/export", async (HttpContext http, IHttpClientFactory clients) =>
        {
            var id = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            using var response = await clients.CreateClient("Backend").GetAsync($"users/{id}/export");
            if (!response.IsSuccessStatusCode)
                return Results.StatusCode((int)response.StatusCode);
            return Results.File(await response.Content.ReadAsByteArrayAsync(), "application/json", "truckerreward-data.json");
        }).RequireAuthorization().AddEndpointFilter(ViewAs.NotWhileViewing);
}
