// the one place the password rules live. register, change and reset all go through Check,
// and the frontend shows Description next to every new-password box
public static class PasswordPolicy
{
    public const int MinLength = 8;

    public const string Description =
        "At least 8 characters with an upper case letter, a lower case letter and a number.";

    // null when the password is acceptable, otherwise a message that names the first rule it broke
    public static string? Check(string? password)
    {
        if (password is null || password.Length < MinLength)
            return $"Password must be at least {MinLength} characters.";
        if (!password.Any(char.IsUpper))
            return "Password needs an upper case letter.";
        if (!password.Any(char.IsLower))
            return "Password needs a lower case letter.";
        if (!password.Any(char.IsDigit))
            return "Password needs a number.";
        return null;
    }
}
