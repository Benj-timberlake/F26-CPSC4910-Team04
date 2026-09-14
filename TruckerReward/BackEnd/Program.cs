var builder = WebApplication.CreateBuilder(args);

// Allow endpoints to make HTTP requests to eBay.
builder.Services.AddHttpClient();
builder.Services.AddSingleton<EbayClient>();

var app = builder.Build();

app.UseHttpsRedirection();

// Register the endpoint defined in src/endpoints/market.cs.
app.MapMarketEndpoints();

app.Run();
