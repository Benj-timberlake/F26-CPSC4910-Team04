using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FrontEnd.Auth;

// turns the backend's user profile into the auth cookie. used at login and again by
// CookieRevalidation when it refreshes the cookie from the current user row
public static class TruckerSignIn
{
    public static ClaimsPrincipal Principal(UserProfile user) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.Username),
        new Claim(ClaimTypes.Email, user.Email),
        new Claim(ClaimTypes.Role, user.UserType)
    ], CookieAuthenticationDefaults.AuthenticationScheme));

    public static Task SignInAsync(HttpContext http, UserProfile user)
    {
        var clock = http.RequestServices.GetRequiredService<TimeProvider>();
        var properties = new AuthenticationProperties();
        CookieRevalidation.MarkChecked(properties, clock.GetUtcNow());
        return http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, Principal(user), properties);
    }
}
