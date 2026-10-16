namespace SublimaLab.Application.Accounts;

public sealed record RegisterRequest(string Email, string Password, string FirstName, string LastName);

public sealed record LoginRequest(string Email, string Password, bool RememberMe);

/// <summary>Second sign-in step: an authenticator code, or one of the account's recovery codes.</summary>
public sealed record TwoFactorLoginRequest(string Code, bool RememberMe, bool IsRecoveryCode);

/// <summary>Either the signed-in user, or a request for the second factor (password already verified).</summary>
public sealed record LoginOutcome(UserDto? User, bool RequiresTwoFactor);

public sealed record TwoFactorStatusDto(bool Enabled, int RecoveryCodesLeft, bool Required);

/// <summary>What an authenticator app needs: the key (formatted for typing) and the otpauth URI as a QR code.</summary>
public sealed record TwoFactorSetupDto(string SharedKey, string AuthenticatorUri, string QrCodeSvg);

public sealed record TwoFactorCodeRequest(string Code);

public sealed record RecoveryCodesDto(IReadOnlyList<string> RecoveryCodes);

public sealed record ConfirmEmailRequest(Guid UserId, string Token);

public sealed record EmailRequest(string Email);

public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record UpdateProfileRequest(string FirstName, string LastName, string? PhoneNumber);

public sealed record UserDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    bool EmailConfirmed,
    IReadOnlyList<string> Roles,
    bool TwoFactorEnabled,
    bool MustEnableTwoFactor);

public sealed record RegisterResult(Guid UserId, string Email, bool RequiresEmailConfirmation);

/// <summary>
/// Account operations backed by ASP.NET Core Identity (implemented in Infrastructure).
/// Sign-in issues the HttpOnly authentication cookie for the current request.
/// </summary>
public interface IIdentityService
{
    Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    Task<LoginOutcome> SignInAsync(LoginRequest request, CancellationToken cancellationToken);

    /// <summary>Completes a sign-in that <see cref="SignInAsync"/> left waiting for the second factor.</summary>
    Task<UserDto> SignInWithTwoFactorAsync(TwoFactorLoginRequest request, CancellationToken cancellationToken);

    Task SignOutAsync();

    Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken);

    /// <summary>Always succeeds silently so the endpoint cannot be used to discover accounts.</summary>
    Task ResendConfirmationAsync(EmailRequest request, CancellationToken cancellationToken);

    /// <summary>Always succeeds silently so the endpoint cannot be used to discover accounts.</summary>
    Task RequestPasswordResetAsync(EmailRequest request, CancellationToken cancellationToken);

    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken);

    Task<UserDto> GetUserAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken);

    Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken);

    Task<TwoFactorStatusDto> GetTwoFactorStatusAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Creates a new authenticator key. Two-step verification stays off until a code is confirmed.</summary>
    Task<TwoFactorSetupDto> BeginTwoFactorSetupAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Confirms the first authenticator code, turns two-step verification on and returns recovery codes.</summary>
    Task<RecoveryCodesDto> EnableTwoFactorAsync(Guid userId, TwoFactorCodeRequest request, CancellationToken cancellationToken);

    Task DisableTwoFactorAsync(Guid userId, TwoFactorCodeRequest request, CancellationToken cancellationToken);

    Task<RecoveryCodesDto> RegenerateRecoveryCodesAsync(Guid userId, TwoFactorCodeRequest request, CancellationToken cancellationToken);
}
