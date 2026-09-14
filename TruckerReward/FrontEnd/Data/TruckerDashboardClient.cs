namespace FrontEnd.Data;

public class TruckerDashboardClient
{
    private readonly HttpClient _httpClient;

    public TruckerDashboardClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<TruckerDashboard?> GetDashboardAsync()
        => await _httpClient.GetFromJsonAsync<TruckerDashboard>("dashboard");
}

public record TruckerDashboard(Trucker Trucker, Points Points, Sponsor Sponsor);

public record Trucker(string Name, string DriverId, string Email, string Phone, string HomeTerminal);

public record Points(int CurrentBalance, int PointsToNextTier, string Tier, string NextMilestone);

public record Sponsor(string Name, string Program, string Benefit, string ContactEmail);
