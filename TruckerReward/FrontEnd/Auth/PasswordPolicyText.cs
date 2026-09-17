using System.Net.Http.Json;

namespace FrontEnd.Auth;

// the rules live in the backend, every page with a new-password box shows the same sentence
public static class PasswordPolicyText
{
    private sealed record Policy(string Description);

    public static async Task<string> Load(IHttpClientFactory clients)
    {
        try
        {
            using var client = clients.CreateClient("Backend");
            var policy = await client.GetFromJsonAsync<Policy>("auth/password-policy");
            return policy?.Description ?? "";
        }
        catch (HttpRequestException)
        {
            return "";
        }
    }
}
