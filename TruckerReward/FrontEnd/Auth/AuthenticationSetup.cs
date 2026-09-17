using Microsoft.AspNetCore.Authentication.Cookies;

namespace FrontEnd.Auth;

public static class AuthenticationSetup
{
    // temporary cookie that holds the google/microsoft identity until we've matched it to a user in our db
    public const string ExternalScheme = "External";

    public static void AddTruckerAuthentication(this WebApplicationBuilder builder)
    {
        var auth = builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/login";
                options.AccessDeniedPath = "/login";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                // re-check the user row every few minutes, see CookieRevalidation
                options.EventsType = typeof(CookieRevalidation);
            })
            .AddCookie(ExternalScheme, options =>
            {
                options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            });

        // sso providers only show up if their keys are set (user secrets locally, env vars on eb)
        var google = builder.Configuration.GetSection("Authentication:Google");
        if (!string.IsNullOrEmpty(google["ClientId"]) && !string.IsNullOrEmpty(google["ClientSecret"]))
        {
            auth.AddGoogle(options =>
            {
                options.ClientId = google["ClientId"]!;
                options.ClientSecret = google["ClientSecret"]!;
                options.SignInScheme = ExternalScheme;
            });
        }

        var microsoft = builder.Configuration.GetSection("Authentication:Microsoft");
        if (!string.IsNullOrEmpty(microsoft["ClientId"]) && !string.IsNullOrEmpty(microsoft["ClientSecret"]))
        {
            auth.AddMicrosoftAccount(options =>
            {
                options.ClientId = microsoft["ClientId"]!;
                options.ClientSecret = microsoft["ClientSecret"]!;
                options.SignInScheme = ExternalScheme;
            });
        }

        builder.Services.AddScoped<CookieRevalidation>();
        builder.Services.AddAuthorization();
    }
}
