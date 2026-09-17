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

// In production the app sits behind CloudFront (TLS) and the beanstalk nginx, so the scheme the
// browser used and its IP arrive in headers. Trusting them is what makes the auth cookie Secure,
// the Google/Microsoft redirect URIs https, and the login attempt log show real addresses.
// See infra/README.md.
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
