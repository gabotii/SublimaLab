using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SublimaLab.Api.Http;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Accounts;

namespace SublimaLab.Api.Controllers;

public sealed record AntiforgeryTokenResponse(string Token, string HeaderName);

public sealed record RegisterResponse(string Email, bool RequiresEmailConfirmation, string Message);

/// <summary>
/// <c>User</c> is null when <c>RequiresTwoFactor</c> is true: the password was accepted and the
/// client must post an authenticator or recovery code to <c>/api/auth/login/2fa</c>.
/// </summary>
public sealed record AuthResponse(UserDto? User, AntiforgeryTokenResponse Antiforgery, bool RequiresTwoFactor = false);

public sealed record SessionResponse(UserDto? User);

[ApiController]
[Route("api/antiforgery")]
public sealed class AntiforgeryController(IAntiforgery antiforgery) : ControllerBase
{
    /// <summary>Issues the CSRF token that must be sent as X-XSRF-TOKEN on unsafe requests.</summary>
    [HttpGet("token")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public ActionResult<AntiforgeryTokenResponse> GetToken() => Issue(antiforgery, HttpContext);

    internal static AntiforgeryTokenResponse Issue(IAntiforgery antiforgery, HttpContext context)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        return new AntiforgeryTokenResponse(tokens.RequestToken!, tokens.HeaderName!);
    }
}

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IIdentityService identity,
    GuestSessionMerger guestSessionMerger,
    CookieClientSession session,
    ICurrentUser currentUser,
    IAntiforgery antiforgery) : ControllerBase
{
    [HttpPost("register")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<RegisterResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await identity.RegisterAsync(request, cancellationToken);
        var message = result.RequiresEmailConfirmation
            ? "Check your inbox to confirm your email address."
            : "Your account is ready. You can sign in now.";
        return Ok(new RegisterResponse(result.Email, result.RequiresEmailConfirmation, message));
    }

    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var outcome = await identity.SignInAsync(request, cancellationToken);
        if (outcome.User is null)
        {
            return Ok(new AuthResponse(null, AntiforgeryController.Issue(antiforgery, HttpContext), RequiresTwoFactor: true));
        }

        return Ok(await CompleteSignInAsync(outcome.User, cancellationToken));
    }

    /// <summary>Second sign-in step for accounts with two-step verification.</summary>
    [HttpPost("login/2fa")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<ActionResult<AuthResponse>> LoginTwoFactor(TwoFactorLoginRequest request, CancellationToken cancellationToken) =>
        Ok(await CompleteSignInAsync(await identity.SignInWithTwoFactorAsync(request, cancellationToken), cancellationToken));

    private async Task<AuthResponse> CompleteSignInAsync(UserDto user, CancellationToken cancellationToken)
    {
        // Keep whatever the visitor built as a guest, then rotate the guest session.
        await guestSessionMerger.MergeAsync(user.Id, session.SessionId, cancellationToken);
        session.Clear();

        return new AuthResponse(user, AntiforgeryController.Issue(antiforgery, HttpContext));
    }

    [HttpPost("logout")]
    public async Task<ActionResult<AntiforgeryTokenResponse>> Logout()
    {
        await identity.SignOutAsync();
        session.Clear();
        return Ok(AntiforgeryController.Issue(antiforgery, HttpContext));
    }

    /// <summary>
    /// Current session for single-page clients: always 200, with <c>user</c> null when signed out,
    /// so checking "am I signed in?" doesn't produce an error response.
    /// </summary>
    [HttpGet("session")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<SessionResponse>> Session(CancellationToken cancellationToken) =>
        Ok(new SessionResponse(currentUser.UserId is { } userId ? await identity.GetUserAsync(userId, cancellationToken) : null));

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserDto>> Me(CancellationToken cancellationToken) =>
        Ok(await identity.GetUserAsync(currentUser.RequireUserId(), cancellationToken));

    [HttpPost("confirm-email")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        await identity.ConfirmEmailAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPost("resend-confirmation")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ResendConfirmation(EmailRequest request, CancellationToken cancellationToken)
    {
        await identity.ResendConfirmationAsync(request, cancellationToken);
        return Accepted();
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ForgotPassword(EmailRequest request, CancellationToken cancellationToken)
    {
        await identity.RequestPasswordResetAsync(request, cancellationToken);
        return Accepted();
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await identity.ResetPasswordAsync(request, cancellationToken);
        return NoContent();
    }
}
