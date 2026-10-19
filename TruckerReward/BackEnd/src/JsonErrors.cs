using Microsoft.AspNetCore.Diagnostics;

// failed requests always answer with a json message
public static class JsonErrors
{
    public const string Malformed = "The request was malformed. Check the fields and try again.";
    public const string Failed = "Something went wrong on our side. Try again in a moment.";

    public static void AddJsonErrors(this WebApplicationBuilder builder) =>
        builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);

    public static void UseJsonErrors(this WebApplication app) =>
        app.UseExceptionHandler(errors => errors.Run(async http =>
        {
            var bad = http.Features.Get<IExceptionHandlerFeature>()?.Error as BadHttpRequestException;
            http.Response.StatusCode = bad?.StatusCode ?? StatusCodes.Status500InternalServerError;
            await http.Response.WriteAsJsonAsync(new { message = bad is null ? Failed : Malformed });
        }));
}
