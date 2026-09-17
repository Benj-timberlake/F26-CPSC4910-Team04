using FrontEnd.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.AddTruckerAuthentication();

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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapAccountEndpoints();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
