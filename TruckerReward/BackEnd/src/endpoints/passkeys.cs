using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackEnd.Models;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

// passkeys (face id, fingerprints, security keys). the browser does the ceremony, this checks it
public static class PasskeyEndpoints
{
    public static readonly TimeSpan CeremonyLifetime = TimeSpan.FromMinutes(5);
    public const int MaxNameLength = 100;

    // the relying party is whatever host FRONTEND_URL names, so passkeys only work on that address
    public static void AddPasskeys(this IServiceCollection services, IConfiguration config)
    {
        services.AddMemoryCache();
        services.AddSingleton<IFido2>(_ =>
        {
            var frontend = new Uri(config["FRONTEND_URL"] ?? "http://localhost:8081");
            return new Fido2(new Fido2Configuration
            {
                RPID = frontend.Host,
                RPName = "TruckerReward",
                Origins = new HashSet<string> { frontend.GetLeftPart(UriPartial.Authority) }
            }, null!);
        });
    }

    public static void MapPasskeyEndpoints(this WebApplication app)
    {
        app.MapGet("/users/{id:int}/passkeys", async (int id, AppDbContext db) =>
            Results.Ok(await db.PasskeyCredentials.AsNoTracking()
                .Where(p => p.UserId == id)
                .OrderBy(p => p.CreatedAt)
                .Select(p => new PasskeySummary(p.Id, p.Name, p.CreatedAt, p.LastUsedAt))
                .ToListAsync()));

        app.MapDelete("/users/{id:int}/passkeys/{passkeyId:int}", async (int id, int passkeyId, AppDbContext db) =>
            await db.PasskeyCredentials.Where(p => p.Id == passkeyId && p.UserId == id).ExecuteDeleteAsync() == 1
                ? Results.NoContent()
                : Results.NotFound());

        app.MapPost("/users/{id:int}/passkeys/begin", async (int id, AppDbContext db, IFido2 fido2, IMemoryCache cache) =>
        {
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
            if (user is null)
                return Results.NotFound();
            var existing = await db.PasskeyCredentials.AsNoTracking().Where(p => p.UserId == id).Select(p => p.CredentialId).ToListAsync();
            var options = fido2.RequestNewCredential(new RequestNewCredentialParams
            {
                User = new Fido2User { Id = Handle(user.Id), Name = user.Username, DisplayName = $"{user.FirstName} {user.LastName}" },
                ExcludeCredentials = existing.Select(c => new PublicKeyCredentialDescriptor(c)).ToList(),
                // discoverable and face id/fingerprint checked, so sign in needs no username
                AuthenticatorSelection = new AuthenticatorSelection { ResidentKey = ResidentKeyRequirement.Required, UserVerification = UserVerificationRequirement.Required },
                AttestationPreference = AttestationConveyancePreference.None
            });
            return Begin(cache, id, options.ToJson());
        });

        app.MapPost("/users/{id:int}/passkeys/complete", async (int id, CompletePasskeyRequest req, AppDbContext db, IFido2 fido2, IMemoryCache cache, TimeProvider clock) =>
        {
            var name = req.Name?.Trim() ?? "";
            if (name.Length is 0 or > MaxNameLength)
                return Results.BadRequest(new { message = $"Name the passkey in up to {MaxNameLength} characters." });
            if (Take(cache, req.RequestId) is not { } pending || pending.UserId != id)
                return Expired();

            RegisteredPublicKeyCredential credential;
            try
            {
                credential = await fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
                {
                    AttestationResponse = req.Response.Deserialize<AuthenticatorAttestationRawResponse>()!,
                    OriginalOptions = CredentialCreateOptions.FromJson(pending.OptionsJson),
                    IsCredentialIdUniqueToUserCallback = async (args, ct) => !await db.PasskeyCredentials.AnyAsync(p => p.CredentialId == args.CredentialId, ct)
                });
            }
            catch (Exception ex) when (ex is Fido2VerificationException or JsonException or ArgumentException)
            {
                return Results.BadRequest(new { message = "That passkey couldn't be verified. Try again." });
            }

            db.PasskeyCredentials.Add(new PasskeyCredential
            {
                UserId = id, CredentialId = credential.Id, PublicKey = credential.PublicKey, SignCount = credential.SignCount,
                Name = name, CreatedAt = clock.GetUtcNow().UtcDateTime
            });
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        app.MapPost("/auth/passkey/begin", (IFido2 fido2, IMemoryCache cache) =>
        {
            var options = fido2.GetAssertionOptions(new GetAssertionOptionsParams
            {
                AllowedCredentials = [],
                UserVerification = UserVerificationRequirement.Required
            });
            return Begin(cache, null, options.ToJson());
        });

        app.MapPost("/auth/passkey/complete", async (PasskeyLoginRequest req, AppDbContext db, IFido2 fido2, IMemoryCache cache, TimeProvider clock) =>
        {
            if (Take(cache, req.RequestId) is not { UserId: null } pending)
                return Expired();

            AuthenticatorAssertionRawResponse response;
            try
            {
                response = req.Response.Deserialize<AuthenticatorAssertionRawResponse>()!;
            }
            catch (JsonException)
            {
                return Results.Unauthorized();
            }
            var stored = await db.PasskeyCredentials.FirstOrDefaultAsync(p => p.CredentialId == response.RawId);
            var user = stored is null ? null : await db.Users.FirstOrDefaultAsync(u => u.Id == stored.UserId);
            if (stored is null || user is null)
                return Results.Unauthorized();

            var now = clock.GetUtcNow().UtcDateTime;
            try
            {
                var result = await fido2.MakeAssertionAsync(new MakeAssertionParams
                {
                    AssertionResponse = response,
                    OriginalOptions = AssertionOptions.FromJson(pending.OptionsJson),
                    StoredPublicKey = stored.PublicKey,
                    StoredSignatureCounter = (uint)stored.SignCount,
                    IsUserHandleOwnerOfCredentialIdCallback = (args, _) => Task.FromResult(args.UserHandle.AsSpan().SequenceEqual(Handle(stored.UserId)))
                });
                stored.SignCount = result.SignCount;
            }
            catch (Fido2VerificationException)
            {
                db.LoginAttempts.Add(new LoginAttempt { Username = user.Username, UserId = user.Id, Succeeded = false, IpAddress = req.IpAddress ?? "", AttemptedAt = now });
                await db.SaveChangesAsync();
                return Results.Unauthorized();
            }

            var active = user.Status == User.Active;
            if (active)
                stored.LastUsedAt = now;
            db.LoginAttempts.Add(new LoginAttempt { Username = user.Username, UserId = user.Id, Succeeded = active, IpAddress = req.IpAddress ?? "", AttemptedAt = now });
            await db.SaveChangesAsync();
            return active
                ? Results.Ok(UserProfile.Of(user))
                : Results.Json(new { message = "This account has been deactivated." }, statusCode: StatusCodes.Status403Forbidden);
        });
    }

    internal static byte[] Handle(int userId) => Encoding.UTF8.GetBytes(userId.ToString());

    private static IResult Begin(IMemoryCache cache, int? userId, string optionsJson)
    {
        var requestId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        cache.Set("passkey:" + requestId, new Pending(userId, optionsJson), CeremonyLifetime);
        return Results.Text($$"""{"requestId":"{{requestId}}","options":{{optionsJson}}}""", "application/json");
    }

    // single use, so a captured response can't be replayed
    private static Pending? Take(IMemoryCache cache, string? requestId)
    {
        if (string.IsNullOrEmpty(requestId) || !cache.TryGetValue("passkey:" + requestId, out Pending? pending))
            return null;
        cache.Remove("passkey:" + requestId);
        return pending;
    }

    private static IResult Expired() =>
        Results.BadRequest(new { message = "That took too long. Try again." });

    private sealed record Pending(int? UserId, string OptionsJson);
}

public record PasskeySummary(int Id, string Name, DateTime CreatedAt, DateTime? LastUsedAt);

public record CompletePasskeyRequest(string? RequestId, string? Name, JsonElement Response);

public record PasskeyLoginRequest(string? RequestId, JsonElement Response, string? IpAddress);
