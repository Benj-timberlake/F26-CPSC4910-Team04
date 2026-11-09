using Amazon;
using Amazon.S3;

namespace FrontEnd.Auth;

public static class CdlCleanup
{
    // a failure here only gets logged
    public static async Task DeleteAsync(IConfiguration config, ILogger logger, string? driverId)
    {
        var bucket = config["CDL_S3_BUCKET"];
        var region = config["CDL_S3_REGION"];
        if (string.IsNullOrWhiteSpace(bucket) || string.IsNullOrWhiteSpace(region) || !int.TryParse(driverId, out var id))
            return;
        try
        {
            using var s3 = new AmazonS3Client(RegionEndpoint.GetBySystemName(region));
            await s3.DeleteObjectAsync(bucket, $"cdl/drivers/{id}/license");
        }
        catch (AmazonS3Exception ex)
        {
            logger.LogError(ex, "Could not delete the CDL file for deleted user {UserId}", id);
        }
    }
}
