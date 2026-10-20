using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace FrontEnd.Auth;

// blocks backend writes while viewing as someone
public sealed class ViewAsReadOnlyHandler(CircuitServices circuit, IHttpContextAccessor http) : DelegatingHandler
{
    public const string Message = "View as is read-only. Return to your own account to make changes.";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // sign in and reset calls aren't made as the viewed user
        var signIn = request.RequestUri?.AbsolutePath.StartsWith("/auth/") == true;
        if (request.Method != HttpMethod.Get && !signIn && await Viewing())
            return new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = JsonContent.Create(new { message = Message }), RequestMessage = request };
        return await base.SendAsync(request, cancellationToken);
    }

    private async Task<bool> Viewing()
    {
        // circuits have no live HttpContext
        if (circuit.Services?.GetService<AuthenticationStateProvider>() is { } auth)
            return ViewAs.IsViewing((await auth.GetAuthenticationStateAsync()).User);
        return http.HttpContext?.User is { } user && ViewAs.IsViewing(user);
    }
}

// exposes circuit services to http handlers
public sealed class CircuitServices
{
    private static readonly AsyncLocal<IServiceProvider?> current = new();

    public IServiceProvider? Services
    {
        get => current.Value;
        set => current.Value = value;
    }
}

public sealed class CircuitServicesHandler(IServiceProvider services, CircuitServices accessor) : CircuitHandler
{
    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(Func<CircuitInboundActivityContext, Task> next) =>
        async context =>
        {
            accessor.Services = services;
            await next(context);
            accessor.Services = null;
        };
}

public static class ViewAsReadOnly
{
    public static IHttpClientBuilder AddViewAsReadOnly(this IHttpClientBuilder client)
    {
        client.Services.AddHttpContextAccessor();
        client.Services.AddSingleton<CircuitServices>();
        client.Services.AddScoped<CircuitHandler, CircuitServicesHandler>();
        client.Services.AddTransient<ViewAsReadOnlyHandler>();
        return client.AddHttpMessageHandler<ViewAsReadOnlyHandler>();
    }
}
