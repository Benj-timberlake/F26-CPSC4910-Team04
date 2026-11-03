using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace FrontEnd.Shared;

// signed-in pages that talk to the backend one action at a time
public abstract class BackendPage : ComponentBase
{
    [CascadingParameter]
    protected Task<AuthenticationState> AuthState { get; set; } = default!;

    [Inject]
    protected IHttpClientFactory HttpClientFactory { get; set; } = default!;

    protected string? id;
    protected bool busy;
    protected string? error;
    protected string? notice;

    protected async Task<ClaimsPrincipal> SignedIn()
    {
        var user = (await AuthState).User;
        id = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return user;
    }

    // a refusal shows failed, or the backend's message when failed is null
    protected async Task Send(Func<HttpClient, Task<HttpResponseMessage>> call, Func<HttpResponseMessage, Task> ok,
        string? failed = null, string crashed = "That didn't work. Reload and try again.")
    {
        busy = true;
        error = notice = null;
        try
        {
            using var client = HttpClientFactory.CreateClient("Backend");
            var response = await call(client);
            if (response.IsSuccessStatusCode)
                await ok(response);
            else
                error = failed ?? await Message(response) ?? "That didn't work.";
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            error = crashed;
        }
        finally
        {
            busy = false;
        }
    }

    protected Task Send(Func<HttpClient, Task<HttpResponseMessage>> call, Action<HttpResponseMessage> ok,
        string? failed = null, string crashed = "That didn't work. Reload and try again.") =>
        Send(call, r => { ok(r); return Task.CompletedTask; }, failed, crashed);

    protected static async Task<string?> Message(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<Problem>())?.Message;

    protected sealed record Problem(string? Message);
}

public sealed record Company(int Id, string Name);
