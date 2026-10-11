using System.Formats.Cbor;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackEnd.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace TruckerReward.Tests;

public sealed class PasskeyTests : IAsyncDisposable
{
    private readonly TestApp app = new();
    private HttpClient client = null!;
    private int bob, sue;

    private async Task Start()
    {
        client = await app.Start();
        using var db = app.Db();
        var users = new[] { User("bob"), User("sue") };
        db.Users.AddRange(users);
        await db.SaveChangesAsync();
        (bob, sue) = (users[0].Id, users[1].Id);
    }

    private static User User(string name) => new()
    {
        Username = name, FirstName = name, LastName = "Test", Email = $"{name}@example.com",
        UserType = "driver", PhoneNumber = "", Address = "", Password = "x"
    };

    private async Task<JsonElement> Post(string url, object? body = null) =>
        (await (await client.PostAsJsonAsync(url, body ?? new { })).Content.ReadFromJsonAsync<JsonElement>());

    private async Task<HttpResponseMessage> Register(int user, SoftAuthenticator key, int completeAs = 0, string name = "iPhone")
    {
        var begin = await Post($"/users/{user}/passkeys/begin");
        // the browser rejects null where it wants a list
        Xunit.Assert.DoesNotContain(":null", begin.GetRawText());
        return await client.PostAsJsonAsync($"/users/{(completeAs == 0 ? user : completeAs)}/passkeys/complete",
            new { requestId = begin.GetProperty("requestId").GetString(), name, response = key.Create(begin.GetProperty("options")) });
    }

    private async Task<(string RequestId, object Response)> Assert(SoftAuthenticator key)
    {
        var begin = await Post("/auth/passkey/begin");
        Xunit.Assert.DoesNotContain(":null", begin.GetRawText());
        return (begin.GetProperty("requestId").GetString()!, key.Get(begin.GetProperty("options")));
    }

    private Task<HttpResponseMessage> SignIn(string requestId, object response) =>
        client.PostAsJsonAsync("/auth/passkey/complete", new { requestId, response, ipAddress = "1.2.3.4" });

    [Fact]
    public async Task RegisteredPasskeySignsInWithoutAUsername()
    {
        await Start();
        var key = new SoftAuthenticator(bob);

        Xunit.Assert.Equal(HttpStatusCode.NoContent, (await Register(bob, key)).StatusCode);
        var (requestId, response) = await Assert(key);
        var signIn = await SignIn(requestId, response);

        Xunit.Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Xunit.Assert.Equal("bob", (await signIn.Content.ReadFromJsonAsync<UserProfile>())!.Username);
        var list = await client.GetFromJsonAsync<List<PasskeySummary>>($"/users/{bob}/passkeys");
        Xunit.Assert.Equal(("iPhone", (DateTime?)app.Clock.GetUtcNow().UtcDateTime), (list!.Single().Name, list.Single().LastUsedAt));
        var attempt = await app.Db().LoginAttempts.SingleAsync();
        Xunit.Assert.Equal((bob, true, "1.2.3.4"), (attempt.UserId!.Value, attempt.Succeeded, attempt.IpAddress));
    }

    [Fact]
    public async Task ChallengesAreSingleUse()
    {
        await Start();
        var key = new SoftAuthenticator(bob);
        await Register(bob, key);
        var (requestId, response) = await Assert(key);

        Xunit.Assert.Equal(HttpStatusCode.OK, (await SignIn(requestId, response)).StatusCode);
        Xunit.Assert.Equal(HttpStatusCode.BadRequest, (await SignIn(requestId, response)).StatusCode);
    }

    [Fact]
    public async Task UnknownPasskeyIsRefused()
    {
        await Start();
        await Register(bob, new SoftAuthenticator(bob));
        var (requestId, response) = await Assert(new SoftAuthenticator(bob));

        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, (await SignIn(requestId, response)).StatusCode);
    }

    [Fact]
    public async Task ForgedSignatureIsRefused()
    {
        await Start();
        var key = new SoftAuthenticator(bob);
        await Register(bob, key);
        var (requestId, _) = await Assert(key);
        var forged = key.Get(JsonSerializer.SerializeToElement(new { challenge = "AAAA" }));

        Xunit.Assert.Equal(HttpStatusCode.Unauthorized, (await SignIn(requestId, forged)).StatusCode);
        Xunit.Assert.False((await app.Db().LoginAttempts.SingleAsync()).Succeeded);
    }

    [Fact]
    public async Task RegistrationIsCheckedAgainstTheSiteAndTheUser()
    {
        await Start();

        Xunit.Assert.Equal(HttpStatusCode.BadRequest, (await Register(bob, new SoftAuthenticator(bob) { Origin = "http://evil" })).StatusCode);
        Xunit.Assert.Equal(HttpStatusCode.BadRequest, (await Register(bob, new SoftAuthenticator(bob), completeAs: sue)).StatusCode);
        Xunit.Assert.Equal(HttpStatusCode.BadRequest, (await Register(bob, new SoftAuthenticator(bob), name: "")).StatusCode);
        Xunit.Assert.Empty(await app.Db().PasskeyCredentials.ToListAsync());
    }

    [Fact]
    public async Task DeactivatedAccountsCantSignIn()
    {
        await Start();
        var key = new SoftAuthenticator(bob);
        await Register(bob, key);
        await app.Db().Users.Where(u => u.Id == bob).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, BackEnd.Models.User.Inactive));
        var (requestId, response) = await Assert(key);

        Xunit.Assert.Equal(HttpStatusCode.Forbidden, (await SignIn(requestId, response)).StatusCode);
    }

    [Fact]
    public async Task UsersOnlyRemoveTheirOwnPasskeys()
    {
        await Start();
        await Register(bob, new SoftAuthenticator(bob));
        var id = (await app.Db().PasskeyCredentials.SingleAsync()).Id;

        Xunit.Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/users/{sue}/passkeys/{id}")).StatusCode);
        Xunit.Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/users/{bob}/passkeys/{id}")).StatusCode);
        Xunit.Assert.Empty(await app.Db().PasskeyCredentials.ToListAsync());
    }

    public ValueTask DisposeAsync() => app.DisposeAsync();

    // stands in for face id: a p-256 key that answers webauthn like a browser would
    private sealed class SoftAuthenticator(int userId)
    {
        private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly byte[] credentialId = RandomNumberGenerator.GetBytes(16);
        private uint counter;
        public string Origin { get; init; } = "http://frontend";

        public object Create(JsonElement options)
        {
            var clientData = ClientData("webauthn.create", options);
            var p = key.ExportParameters(false);
            var cose = new CborWriter();
            cose.WriteStartMap(5);
            cose.WriteInt32(1); cose.WriteInt32(2);
            cose.WriteInt32(3); cose.WriteInt32(-7);
            cose.WriteInt32(-1); cose.WriteInt32(1);
            cose.WriteInt32(-2); cose.WriteByteString(p.Q.X);
            cose.WriteInt32(-3); cose.WriteByteString(p.Q.Y);
            cose.WriteEndMap();
            var authData = AuthData(0x45, [.. new byte[16], (byte)(credentialId.Length >> 8), (byte)credentialId.Length, .. credentialId, .. cose.Encode()]);
            var attestation = new CborWriter();
            attestation.WriteStartMap(3);
            attestation.WriteTextString("fmt"); attestation.WriteTextString("none");
            attestation.WriteTextString("attStmt"); attestation.WriteStartMap(0); attestation.WriteEndMap();
            attestation.WriteTextString("authData"); attestation.WriteByteString(authData);
            attestation.WriteEndMap();
            return new
            {
                id = B64(credentialId), rawId = B64(credentialId), type = "public-key", clientExtensionResults = new { },
                response = new { attestationObject = B64(attestation.Encode()), clientDataJSON = B64(clientData), transports = new[] { "internal" } }
            };
        }

        public object Get(JsonElement options)
        {
            var clientData = ClientData("webauthn.get", options);
            var authData = AuthData(0x05, []);
            var signature = key.SignData([.. authData, .. SHA256.HashData(clientData)], HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            return new
            {
                id = B64(credentialId), rawId = B64(credentialId), type = "public-key", clientExtensionResults = new { },
                response = new { authenticatorData = B64(authData), clientDataJSON = B64(clientData), signature = B64(signature), userHandle = B64(Encoding.UTF8.GetBytes(userId.ToString())) }
            };
        }

        private byte[] ClientData(string type, JsonElement options) => JsonSerializer.SerializeToUtf8Bytes(new
        {
            type, challenge = options.GetProperty("challenge").GetString(), origin = Origin, crossOrigin = false
        });

        private byte[] AuthData(byte flags, byte[] rest)
        {
            counter++;
            return [.. SHA256.HashData("frontend"u8), flags, (byte)(counter >> 24), (byte)(counter >> 16), (byte)(counter >> 8), (byte)counter, .. rest];
        }

        private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
