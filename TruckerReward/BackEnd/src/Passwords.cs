using BackEnd.Models;
using Microsoft.AspNetCore.Identity;

// hashing and verifying, shared by login, register, change and reset
public static class Passwords
{
    // PBKDF2 with a random salt per user, built into asp.net
    private static readonly PasswordHasher<User> hasher = new();

    // verifying against a missing user still costs one hash, so timing can't reveal real usernames
    private static readonly User nobody = new() { UserType = AuthEndpoints.Driver, Username = "", Email = "", PhoneNumber = "", Address = "" };
    private static readonly string nobodysHash = hasher.HashPassword(nobody, Guid.NewGuid().ToString());

    public static string Hash(User user, string password) => hasher.HashPassword(user, password);

    public static bool Verify(User? user, string? password)
    {
        var result = hasher.VerifyHashedPassword(user ?? nobody, user?.Password ?? nobodysHash, password ?? "");
        return result != PasswordVerificationResult.Failed && user?.Password is not null;
    }
}
