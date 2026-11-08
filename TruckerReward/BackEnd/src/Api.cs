// the endpoints Program.cs and the tests both map
public static class Api
{
    public static void MapApi(this WebApplication app)
    {
        app.MapAuthEndpoints();
        app.MapPasswordEndpoints();
        app.MapEmailConfirmEndpoints();
        app.MapSignInCodeEndpoints();
        app.MapSessionEndpoints();
        app.MapUserEndpoints();
        app.MapAdminEndpoints();
        app.MapNotificationEndpoints();
        app.MapPointsEndpoints();
        app.MapAuditEndpoints();
        app.MapViewAsEndpoints();
        app.MapPasskeyEndpoints();
        app.MapSponsorTeamEndpoints();
        app.MapMassAccountCreationEndpoints();
    }
}
