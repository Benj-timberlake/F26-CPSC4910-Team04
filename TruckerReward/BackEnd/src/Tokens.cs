using System.Security.Cryptography;
using System.Text;

// random url-safe tokens. only their hash is stored
public static class Tokens
{
    public static string New() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string Link(IConfiguration config, string path, string token) =>
        $"{config["FRONTEND_URL"]?.TrimEnd('/')}/{path}?token={token}";
}
