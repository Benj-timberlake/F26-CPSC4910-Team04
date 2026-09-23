using System.Security.Cryptography;
using System.Text;

// with BACKEND_API_KEY set, every request needs the same value in X-Api-Key. unset means open.
public static class ApiKeyMiddleware
{
    public const string Header = "X-Api-Key";
    public const string ConfigKey = "BACKEND_API_KEY";

    private static readonly string[] openPaths = ["/", "/health"];

    public static WebApplication UseBackendApiKey(this WebApplication app)
    {
        var expected = app.Configuration[ConfigKey];
        if (string.IsNullOrEmpty(expected))
        {
            app.Logger.LogWarning("{Key} is not set, the backend accepts requests from anyone", ConfigKey);
            return app;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path.Value ?? "/";
            var open = openPaths.Contains(path, StringComparer.OrdinalIgnoreCase)
                || (app.Environment.IsDevelopment() && (path.StartsWith("/scalar") || path.StartsWith("/openapi")));

            if (!open && !Matches(context.Request.Headers[Header], expectedBytes))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { message = "Missing or invalid API key." });
                return;
            }
            await next(context);
        });
        return app;
    }

    private static bool Matches(string? provided, byte[] expected) =>
        provided is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), expected);
}
