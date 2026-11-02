using Amazon;
using Amazon.S3;
using Amazon.S3.Model;

namespace FrontEnd;

public sealed class CdlStorage(IConfiguration config)
{
    public const int MaxBytes = 10 * 1024 * 1024;

    public static async Task<MemoryStream> ReadAsync(Stream source, CancellationToken cancellationToken = default)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int count;
        while ((count = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + count > MaxBytes)
            {
                buffer.Dispose();
                throw new InvalidDataException("CDL files must be 10 MB or smaller.");
            }
            buffer.Write(chunk, 0, count);
        }
        try { ContentType(buffer); }
        catch { buffer.Dispose(); throw; }
        buffer.Position = 0;
        return buffer;
    }

    private static string ContentType(MemoryStream file)
    {
        var bytes = file.GetBuffer().AsSpan(0, (int)file.Length);
        if (bytes.StartsWith("%PDF-"u8)) return "application/pdf";
        if (bytes.StartsWith(new byte[] { 0xff, 0xd8, 0xff })) return "image/jpeg";
        if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        throw new InvalidDataException("Choose a PDF, JPEG, or PNG file.");
    }

    public async Task UploadAsync(int driverId, MemoryStream file, CancellationToken cancellationToken = default)
    {
        if (driverId <= 0) throw new InvalidDataException("Sign in as a driver to upload your CDL.");
        var bucket = config["CDL_S3_BUCKET"];
        var region = config["CDL_S3_REGION"];
        if (string.IsNullOrWhiteSpace(bucket) || string.IsNullOrWhiteSpace(region))
            throw new InvalidOperationException("CDL storage is not configured.");
        using var s3 = new AmazonS3Client(RegionEndpoint.GetBySystemName(region));
        file.Position = 0;
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = $"cdl/drivers/{driverId}/license",
            InputStream = file,
            ContentType = ContentType(file),
            ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256,
            AutoCloseStream = false
        }, cancellationToken);
    }
}
