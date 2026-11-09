namespace FrontEnd.Auth;

// a real check for the deploy, unlike the fallback page
public static class HealthCheck
{
    public static void MapHealthCheck(this WebApplication app) =>
        app.MapGet("/health", async (IHttpClientFactory clients) =>
        {
            try
            {
                using var response = await clients.CreateClient("Backend").GetAsync("health");
                return response.IsSuccessStatusCode
                    ? Results.Text("ok")
                    : Results.Text($"backend returned {(int)response.StatusCode}", statusCode: 503);
            }
            catch (HttpRequestException)
            {
                return Results.Text("backend unreachable", statusCode: 503);
            }
        });
}
