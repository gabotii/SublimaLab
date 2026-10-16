using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using SublimaLab.Blazor.Models;

namespace SublimaLab.Blazor.Services;

/// <summary>
/// Authentication state backed by the server session cookie. The browser never sees a token:
/// the current user is simply asked of <c>GET /api/auth/session</c>.
/// </summary>
public sealed class AuthService(ApiClient api) : AuthenticationStateProvider
{
    private Task<User?>? _loadUser;

    public User? CurrentUser { get; private set; }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        _loadUser ??= LoadUserAsync();
        CurrentUser = await _loadUser;
        return BuildState(CurrentUser);
    }

    /// <returns>The signed-in user, or null when the account needs a second factor (see <see cref="LoginTwoFactorAsync"/>).</returns>
    public async Task<User?> LoginAsync(string email, string password, bool rememberMe)
    {
        var response = await api.PostAsync<AuthResponse>("api/auth/login", new LoginRequest(email, password, rememberMe));
        api.UseAntiforgeryToken(response.Antiforgery.Token);
        if (response.RequiresTwoFactor || response.User is null)
        {
            return null;
        }

        SetUser(response.User);
        return response.User;
    }

    public async Task<User> LoginTwoFactorAsync(string code, bool rememberMe, bool isRecoveryCode)
    {
        var response = await api.PostAsync<AuthResponse>("api/auth/login/2fa", new TwoFactorLoginRequest(code, rememberMe, isRecoveryCode));
        api.UseAntiforgeryToken(response.Antiforgery.Token);
        SetUser(response.User);
        return response.User!;
    }

    /// <summary>Re-reads the session, e.g. after security settings changed what the server reports.</summary>
    public async Task RefreshAsync()
    {
        _loadUser = LoadUserAsync();
        SetUser(await _loadUser);
    }

    public async Task LogoutAsync()
    {
        var token = await api.PostAsync<AntiforgeryToken>("api/auth/logout");
        api.UseAntiforgeryToken(token.Token);
        SetUser(null);
    }

    public void SetUser(User? user)
    {
        CurrentUser = user;
        _loadUser = Task.FromResult(user);
        NotifyAuthenticationStateChanged(Task.FromResult(BuildState(user)));
    }

    private async Task<User?> LoadUserAsync()
    {
        try
        {
            return (await api.GetAsync<SessionResponse>("api/auth/session")).User;
        }
        catch (Exception e) when (e is ApiException or HttpRequestException)
        {
            return null;
        }
    }

    private static AuthenticationState BuildState(User? user)
    {
        if (user is null)
        {
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
        };
        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "SublimaLabCookie")));
    }
}
