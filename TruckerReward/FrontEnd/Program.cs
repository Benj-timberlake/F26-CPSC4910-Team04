var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// Connect the frontend to your backend.
builder.Services.AddHttpClient("Backend", client =>
{
    var url = builder.Configuration["BACKEND_URL"]
        ?? throw new InvalidOperationException("BACKEND_URL is not set");

    client.BaseAddress = new Uri(url);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();