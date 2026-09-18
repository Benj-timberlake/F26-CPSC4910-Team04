using FrontEnd.Auth;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.AddTruckerAuthentication();
builder.Services.AddSingleton(TimeProvider.System);

void ConfigureBackendClient(HttpClient client)
{
    var url = builder.Configuration["BACKEND_URL"]
        ?? throw new InvalidOperationException("BACKEND_URL is not set");

    client.BaseAddress = new Uri(url);

    // has to match BACKEND_API_KEY on the backend, leave both unset for local dev
    var apiKey = builder.Configuration["BACKEND_API_KEY"];
    if (!string.IsNullOrEmpty(apiKey))
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
}

builder.Services.AddHttpClient("Backend", ConfigureBackendClient);

var app = builder.Build();

// behind cloudfront and the beanstalk nginx the browser's scheme and ip only arrive in headers
if (!app.Environment.IsDevelopment())
{
    var forwarded = new ForwardedHeadersOptions
    {
        ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
        ForwardedProtoHeaderName = "CloudFront-Forwarded-Proto",
        ForwardLimit = null
    };
    forwarded.KnownNetworks.Clear();
    forwarded.KnownProxies.Clear();
    app.UseForwardedHeaders(forwarded);

    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapAccountEndpoints();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
