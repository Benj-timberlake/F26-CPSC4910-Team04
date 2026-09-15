# C# tests

From the repository root, run with the .NET 10 SDK:

```powershell
dotnet test TruckerReward/SampleApp.sln
```

The first run restores NuGet packages. The suite uses xUnit, bUnit for the
Blazor product grid, and ASP.NET Core TestServer for `/api/products`.

- Grid: titles, images and missing-image placeholders; case-insensitive,
  trimmed searches; empty results; missing files; invalid JSON and retry.
- eBay endpoint: encoded search queries, result limit, bearer authentication,
  marketplace header, JSON response forwarding, missing credentials, missing
  query parameters, and upstream 401/403/429/500 errors mapped to HTTP 502.

Grid tests use isolated temporary sample files because the current catalog
reads `sample-products.json`. API tests replace outbound HTTP with a stub:
no eBay credentials or live requests are needed. These tests do not verify
browser layout at different screen sizes or live eBay availability.
