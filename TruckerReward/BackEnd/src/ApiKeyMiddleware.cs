using System.Security.Cryptography;
using System.Text;

// Only the frontend is supposed to talk to the backend. When BACKEND_API_KEY is set (env var on
// Elastic Beanstalk, user secrets locally, never appsettings) every request has to send the same
// value in X-Api-Key. Left unset, nothing changes, so local dev keeps working without it.
public static class ApiKeyMiddleware
{
    public const string Header = "X-Api-Key";
    public const string ConfigKey = "BACKEND_API_KEY";

    // beanstalk and humans hit these without a key
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

    // constant time so the comparison doesn't leak how much of the key was right
    private static bool Matches(string? provided, byte[] expected) =>
        provided is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), expected);
}
