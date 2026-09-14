namespace FrontEnd.Data;

public class TruckerDashboardClient
{
    private readonly HttpClient _httpClient;

    public TruckerDashboardClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<UserProfile?> GetDashboardAsync()
        => await _httpClient.GetFromJsonAsync<UserProfile>("dashboard");
}

public record UserProfile(
    int Id,
    string UserType,
    string Username,
    string Email,
    string PhoneNumber,
    string Address);
