using System.Net;
using System.Security.Claims;
using Amazon.S3;

namespace FrontEnd.Auth;

public static class CdlReviewEndpoints
{
    public static void MapCdlReview(this WebApplication app) =>
        app.MapGet("/applications/{applicationId:int}/cdl", async Task<IResult> (
            int applicationId, HttpContext http, IHttpClientFactory clients, ICdlReader storage, ILogger<CdlStorage> log) =>
        {
            http.Response.Headers.CacheControl = "no-store, private";
            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
            var id = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(id, out _) || http.User.HasClaim(c => c.Type == ViewAs.ViewerIdClaim))
                return Results.Forbid();
            try
            {
                // Recheck the current company-scoped list on every document request.
                using var client = clients.CreateClient("Backend");
                using var response = await client.GetAsync($"users/{id}/applications/driver", http.RequestAborted);
                if (!response.IsSuccessStatusCode)
                    return Results.Text("You cannot review this CDL.", statusCode: StatusCodes.Status403Forbidden);
                var applications = await response.Content.ReadFromJsonAsync<List<VisibleApplication>>(http.RequestAborted);
                var application = applications?.FirstOrDefault(a => a.Id == applicationId);
                if (application is null)
                    return Results.Text("Application not found or access denied.", statusCode: StatusCodes.Status404NotFound);
                var document = await storage.DownloadAsync(application.ApplicantId, http.RequestAborted);
                return Results.File(document.Bytes, document.ContentType, enableRangeProcessing: true);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.ErrorCode == "NoSuchKey")
            {
                return Results.Text("This driver has not uploaded a CDL license.", statusCode: StatusCodes.Status404NotFound);
            }
            catch (Exception ex) when (ex is AmazonS3Exception or HttpRequestException or InvalidOperationException or InvalidDataException or System.Text.Json.JsonException)
            {
                log.LogError(ex, "CDL review failed for application {ApplicationId}", applicationId);
                return Results.Text("The CDL could not be loaded. It may not have been uploaded yet. Please try again later.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }).RequireAuthorization(policy => policy.RequireRole("sponsor", "admin"));

    private sealed record VisibleApplication(int Id, int ApplicantId);
}
