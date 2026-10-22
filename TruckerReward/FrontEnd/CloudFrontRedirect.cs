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
        var host = new Uri(target).Host;
        return app.Use((context, next) =>
        {
            var request = context.Request;
            // passkeys and cookies only work on the one public host
            if (request.Headers.ContainsKey(Header) && request.Host.Host.Equals(host, StringComparison.OrdinalIgnoreCase))
                return next(context);

            context.Response.Redirect(target + request.PathBase + request.Path + request.QueryString, permanent: true, preserveMethod: true);
            return Task.CompletedTask;
        });
    }
}
