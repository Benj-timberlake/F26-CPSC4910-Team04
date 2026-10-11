using System.Security.Claims;

namespace FrontEnd.Auth;

// proxies the csv download for the browser
public static class AuditExport
{
    private static readonly string[] Filters = ["category", "from", "to", "companyId", "driverId"];

    public static void MapAuditExport(this WebApplication app) =>
        app.MapGet("/audit/export", async (HttpContext http, IHttpClientFactory clients) =>
        {
            var id = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var query = string.Join("&", Filters
                .Where(f => !string.IsNullOrEmpty(http.Request.Query[f]))
                .Select(f => $"{f}={Uri.EscapeDataString(http.Request.Query[f]!)}"));
            using var response = await clients.CreateClient("Backend").GetAsync($"users/{id}/audit.csv" + (query == "" ? "" : "?" + query));
            if (!response.IsSuccessStatusCode)
                return Results.StatusCode((int)response.StatusCode);
            return Results.File(await response.Content.ReadAsByteArrayAsync(), "text/csv", "audit-log.csv");
        }).RequireAuthorization(policy => policy.RequireRole("sponsor", "admin"));
}
