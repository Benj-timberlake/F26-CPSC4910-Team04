namespace FrontEnd;

public static class CloudFrontRedirect
{
    // missing means the request skipped cloudfront
    public const string Header = "CloudFront-Forwarded-Proto";

    public static IApplicationBuilder UseCloudFrontRedirect(this IApplicationBuilder app, string? publicUrl)
    {
        if (string.IsNullOrEmpty(publicUrl) || !publicUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return app;

        var target = publicUrl.TrimEnd('/');
        return app.Use((context, next) =>
        {
            if (context.Request.Headers.ContainsKey(Header))
                return next(context);

            var request = context.Request;
            context.Response.Redirect(target + request.PathBase + request.Path + request.QueryString, permanent: true, preserveMethod: true);
            return Task.CompletedTask;
        });
    }
}
