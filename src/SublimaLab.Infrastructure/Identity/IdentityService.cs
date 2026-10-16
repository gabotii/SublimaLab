using System.Net;
using System.Security.Claims;
using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using QRCoder;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Accounts;
using SublimaLab.Application.Common;

namespace SublimaLab.Infrastructure.Identity;

/// <summary>
/// ASP.NET Core Identity implementation of account flows. Authentication uses an HttpOnly,
/// Secure, SameSite cookie, so no token is ever exposed to JavaScript.
/// </summary>
internal sealed class IdentityService(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signInManager,
    IHttpContextAccessor httpContextAccessor,
    IEmailSender emailSender,
    IOptions<AppUrlOptions> urls,
    IOptions<AuthOptions> authOptions,
    IValidator<RegisterRequest> registerValidator,
    IValidator<LoginRequest> loginValidator,
    IValidator<ConfirmEmailRequest> confirmValidator,
    IValidator<EmailRequest> emailValidator,
    IValidator<ResetPasswordRequest> resetValidator,
    IValidator<ChangePasswordRequest> changePasswordValidator,
    IValidator<UpdateProfileRequest> profileValidator,
    TimeProvider clock,
    ILogger<IdentityService> logger) : IIdentityService
{
    private const int RecoveryCodeCount = 10;

    public async Task<RegisterResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        await registerValidator.ValidateOrThrowAsync(request, cancellationToken);
        var email = request.Email.Trim();
        var requiresConfirmation = signInManager.Options.SignIn.RequireConfirmedEmail;

        var existing = await users.FindByEmailAsync(email);
        if (existing is not null)
        {
            // Same response as a new registration, so the endpoint cannot reveal who has an account.
            await SendAsync(email, "You already have a SublimaLab account",
                "Someone (hopefully you) tried to create a SublimaLab account with this email address. " +
                $"You already have one. You can <a href=\"{Link("/account/login")}\">sign in</a> or " +
                $"<a href=\"{Link("/account/forgot-password")}\">reset your password</a>.", cancellationToken);
            return new RegisterResult(Guid.Empty, email, requiresConfirmation);
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };

        var result = await users.CreateAsync(user, request.Password);
        ThrowIfFailed(result, "password");
        await users.AddToRoleAsync(user, Roles.Customer);

        await SendConfirmationEmailAsync(user, cancellationToken);
        logger.LogInformation("New customer registered: {UserId}", user.Id);
        return new RegisterResult(user.Id, email, requiresConfirmation);
    }

    public async Task<LoginOutcome> SignInAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        await loginValidator.ValidateOrThrowAsync(request, cancellationToken);
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            throw InvalidCredentials();
        }

        var result = await signInManager.PasswordSignInAsync(user, request.Password, request.RememberMe, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            logger.LogWarning("Locked-out account {UserId} attempted to sign in", user.Id);
            throw new ForbiddenException("ACCOUNT_LOCKED", "Too many failed attempts. Please try again in 15 minutes or reset your password.");
        }

        if (result.IsNotAllowed)
        {
            throw new ForbiddenException("EMAIL_NOT_CONFIRMED", "Please confirm your email address before signing in. Check your inbox for the confirmation link.");
        }

        if (result.RequiresTwoFactor)
        {
            // The password was right; Identity set a short-lived cookie naming the user until
            // the second factor arrives.
            return new LoginOutcome(null, RequiresTwoFactor: true);
        }

        if (!result.Succeeded)
        {
            throw InvalidCredentials();
        }

        // Make the new identity visible for the rest of this request (e.g. to issue an
        // antiforgery token bound to the signed-in user).
        SetCurrentPrincipal(await signInManager.CreateUserPrincipalAsync(user));
        return new LoginOutcome(await ToDtoAsync(user), RequiresTwoFactor: false);
    }

    public async Task<UserDto> SignInWithTwoFactorAsync(TwoFactorLoginRequest request, CancellationToken cancellationToken)
    {
        var user = await signInManager.GetTwoFactorAuthenticationUserAsync()
            ?? throw new UnauthorizedException("TWO_FACTOR_SESSION_EXPIRED", "Your sign-in took too long. Please enter your password again.");

        var result = request.IsRecoveryCode
            ? await signInManager.TwoFactorRecoveryCodeSignInAsync(NormalizeRecoveryCode(request.Code))
            : await signInManager.TwoFactorAuthenticatorSignInAsync(NormalizeAuthenticatorCode(request.Code), request.RememberMe, rememberClient: false);

        if (result.IsLockedOut)
        {
            logger.LogWarning("Account {UserId} locked after failed two-step codes", user.Id);
            throw new ForbiddenException("ACCOUNT_LOCKED", "Too many failed attempts. Please try again in 15 minutes.");
        }

        if (!result.Succeeded)
        {
            throw InvalidCode();
        }

        if (request.IsRecoveryCode)
        {
            logger.LogWarning("User {UserId} signed in with a recovery code", user.Id);
        }

        SetCurrentPrincipal(await CreateMultiFactorPrincipalAsync(user));
        return await ToDtoAsync(user);
    }

    public async Task SignOutAsync()
    {
        await signInManager.SignOutAsync();
        SetCurrentPrincipal(new ClaimsPrincipal(new ClaimsIdentity()));
    }

    public async Task ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        await confirmValidator.ValidateOrThrowAsync(request, cancellationToken);
        var user = await users.FindByIdAsync(request.UserId.ToString());
        if (user is null || DecodeToken(request.Token) is not { } token)
        {
            throw InvalidToken();
        }

        if (user.EmailConfirmed)
        {
            return;
        }

        var result = await users.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
        {
            throw InvalidToken();
        }
    }

    public async Task ResendConfirmationAsync(EmailRequest request, CancellationToken cancellationToken)
    {
        await emailValidator.ValidateOrThrowAsync(request, cancellationToken);
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is { EmailConfirmed: false })
        {
            await SendConfirmationEmailAsync(user, cancellationToken);
        }
    }

    public async Task RequestPasswordResetAsync(EmailRequest request, CancellationToken cancellationToken)
    {
        await emailValidator.ValidateOrThrowAsync(request, cancellationToken);
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
        {
            return;
        }

        var token = EncodeToken(await users.GeneratePasswordResetTokenAsync(user));
        var link = Link($"/account/reset-password?email={Uri.EscapeDataString(user.Email!)}&token={token}");
        await SendAsync(user.Email!, "Reset your SublimaLab password",
            $"Hi {WebUtility.HtmlEncode(user.FirstName)}, we received a request to reset your password. " +
            $"<a href=\"{link}\">Choose a new password</a>. If you did not request this, you can ignore this email.", cancellationToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await resetValidator.ValidateOrThrowAsync(request, cancellationToken);
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || DecodeToken(request.Token) is not { } token)
        {
            throw InvalidToken();
        }

        var result = await users.ResetPasswordAsync(user, token, request.NewPassword);
        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
        {
            throw InvalidToken();
        }

        ThrowIfFailed(result, "newPassword");

        // A successful reset also proves ownership of the mailbox.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await users.UpdateAsync(user);
        }

        await users.ResetAccessFailedCountAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        logger.LogInformation("Password reset for user {UserId}", user.Id);
    }

    public async Task<UserDto> GetUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await ToDtoAsync(await FindRequiredAsync(userId));

    public async Task<UserDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        await profileValidator.ValidateOrThrowAsync(request, cancellationToken);
        var user = await FindRequiredAsync(userId);
        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
        ThrowIfFailed(await users.UpdateAsync(user), "profile");
        return await ToDtoAsync(user);
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await changePasswordValidator.ValidateOrThrowAsync(request, cancellationToken);
        var user = await FindRequiredAsync(userId);
        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
        {
            throw RequestValidationException.For("currentPassword", "The current password is incorrect.");
        }

        ThrowIfFailed(result, "newPassword");

        // The security stamp changed: re-issue this session's cookie, other sessions are invalidated.
        await signInManager.RefreshSignInAsync(user);
    }

    public async Task<TwoFactorStatusDto> GetTwoFactorStatusAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await FindRequiredAsync(userId);
        var enabled = await users.GetTwoFactorEnabledAsync(user);
        return new TwoFactorStatusDto(enabled, enabled ? await users.CountRecoveryCodesAsync(user) : 0, await IsTwoFactorRequiredAsync(user));
    }

    public async Task<TwoFactorSetupDto> BeginTwoFactorSetupAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await FindRequiredAsync(userId);
        if (await users.GetTwoFactorEnabledAsync(user))
        {
            throw new ConflictException("TWO_FACTOR_ALREADY_ENABLED", "Two-step verification is already on. Turn it off first to set up a new authenticator.");
        }

        // A fresh key per attempt, so an abandoned setup screen can never be completed later.
        await users.ResetAuthenticatorKeyAsync(user);
        await signInManager.RefreshSignInAsync(user);

        var key = await users.GetAuthenticatorKeyAsync(user) ?? throw new InvalidOperationException("Authenticator key was not created.");
        var issuer = Uri.EscapeDataString(authOptions.Value.AuthenticatorIssuer);
        var uri = $"otpauth://totp/{issuer}:{Uri.EscapeDataString(user.Email!)}?secret={key}&issuer={issuer}&digits=6";

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);
        var svg = new SvgQRCode(data).GetGraphic(5, "#000000", "#ffffff", drawQuietZones: true, sizingMode: SvgQRCode.SizingMode.ViewBoxAttribute);

        return new TwoFactorSetupDto(FormatKey(key), uri, svg);
    }

    public async Task<RecoveryCodesDto> EnableTwoFactorAsync(Guid userId, TwoFactorCodeRequest request, CancellationToken cancellationToken)
    {
        var user = await FindRequiredAsync(userId);
        if (await users.GetTwoFactorEnabledAsync(user))
        {
            throw new ConflictException("TWO_FACTOR_ALREADY_ENABLED", "Two-step verification is already on.");
        }

        if (await users.GetAuthenticatorKeyAsync(user) is null || !await VerifyAuthenticatorCodeAsync(user, request.Code))
        {
            throw InvalidCode();
        }

        await users.SetTwoFactorEnabledAsync(user, true);
        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount) ?? [];

        // The code just proved possession of the authenticator, so this session counts as
        // multi-factor. Turning 2FA on changed the security stamp, which ends other sessions.
        await signInManager.SignInWithClaimsAsync(user, await IsPersistentSessionAsync(), [new Claim(MultiFactor.ClaimType, MultiFactor.ClaimValue)]);
        SetCurrentPrincipal(await CreateMultiFactorPrincipalAsync(user));

        logger.LogInformation("Two-step verification enabled for user {UserId}", user.Id);
        await SendAsync(user.Email!, "Two-step verification is on",
            $"Hi {WebUtility.HtmlEncode(user.FirstName)}, two-step verification was turned on for your SublimaLab account. " +
            "If this wasn't you, reset your password right away.", cancellationToken);
        return new RecoveryCodesDto([.. codes]);
    }

    public async Task DisableTwoFactorAsync(Guid userId, TwoFactorCodeRequest request, CancellationToken cancellationToken)
    {
        var user = await FindRequiredAsync(userId);
        if (!await users.GetTwoFactorEnabledAsync(user))
        {
            return;
        }

        if (await IsTwoFactorRequiredAsync(user))
        {
            throw new BusinessRuleException("TWO_FACTOR_REQUIRED", "Administrator accounts must keep two-step verification on.");
        }

        if (!await VerifyAuthenticatorCodeAsync(user, request.Code) && !(await users.RedeemTwoFactorRecoveryCodeAsync(user, NormalizeRecoveryCode(request.Code))).Succeeded)
        {
            throw InvalidCode();
        }

        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        await signInManager.RefreshSignInAsync(user);

        logger.LogInformation("Two-step verification disabled for user {UserId}", user.Id);
        await SendAsync(user.Email!, "Two-step verification is off",
            $"Hi {WebUtility.HtmlEncode(user.FirstName)}, two-step verification was turned off for your SublimaLab account. " +
            "If this wasn't you, reset your password and turn it back on.", cancellationToken);
    }

    public async Task<RecoveryCodesDto> RegenerateRecoveryCodesAsync(Guid userId, TwoFactorCodeRequest request, CancellationToken cancellationToken)
    {
        var user = await FindRequiredAsync(userId);
        if (!await users.GetTwoFactorEnabledAsync(user))
        {
            throw new BusinessRuleException("TWO_FACTOR_NOT_ENABLED", "Turn on two-step verification first.");
        }

        if (!await VerifyAuthenticatorCodeAsync(user, request.Code))
        {
            throw InvalidCode();
        }

        var codes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount) ?? [];
        logger.LogInformation("Recovery codes regenerated for user {UserId}", user.Id);
        return new RecoveryCodesDto([.. codes]);
    }

    private async Task<bool> VerifyAuthenticatorCodeAsync(ApplicationUser user, string code) =>
        await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, NormalizeAuthenticatorCode(code));

    private async Task<bool> IsTwoFactorRequiredAsync(ApplicationUser user) =>
        authOptions.Value.RequireAdminTwoFactor && await users.IsInRoleAsync(user, Roles.Admin);

    private async Task<bool> IsPersistentSessionAsync()
    {
        var context = httpContextAccessor.HttpContext;
        var current = context is null ? null : await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        return current?.Properties?.IsPersistent ?? false;
    }

    private async Task<ClaimsPrincipal> CreateMultiFactorPrincipalAsync(ApplicationUser user)
    {
        var principal = await signInManager.CreateUserPrincipalAsync(user);
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim(MultiFactor.ClaimType, MultiFactor.ClaimValue));
        return principal;
    }

    /// <summary>Authenticator apps show "123 456" or "123-456"; only the digits matter.</summary>
    private static string NormalizeAuthenticatorCode(string? code) =>
        new((code ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());

    /// <summary>Identity stores recovery codes as upper-case "XXXXX-XXXXX"; accept them typed without the dash or in lower case.</summary>
    private static string NormalizeRecoveryCode(string? code)
    {
        var compact = new string((code ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
        return compact.Length == 10 ? $"{compact[..5]}-{compact[5..]}" : compact;
    }

    /// <summary>Groups the base32 key in blocks of four so it's easier to type into an app.</summary>
    private static string FormatKey(string key) =>
        string.Join(' ', key.ToLowerInvariant().Chunk(4).Select(chunk => new string(chunk)));

    private async Task SendConfirmationEmailAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var token = EncodeToken(await users.GenerateEmailConfirmationTokenAsync(user));
        var link = Link($"/account/confirm-email?userId={user.Id}&token={token}");
        await SendAsync(user.Email!, "Confirm your SublimaLab account",
            $"Welcome to SublimaLab, {WebUtility.HtmlEncode(user.FirstName)}! " +
            $"Please <a href=\"{link}\">confirm your email address</a> to start creating.", cancellationToken);
    }

    private async Task SendAsync(string to, string subject, string bodyHtml, CancellationToken cancellationToken)
    {
        var html = $"""
            <!doctype html>
            <html><body style="font-family:Arial,sans-serif;color:#1f2330;line-height:1.5">
            <h2 style="color:#8b2cf5">SublimaLab</h2>
            <p>{bodyHtml}</p>
            <p style="color:#6b7080;font-size:12px">You received this email because of activity on sublimalab.</p>
            </body></html>
            """;
        var text = System.Text.RegularExpressions.Regex.Replace(bodyHtml, "<[^>]+>", string.Empty);
        await emailSender.SendAsync(new EmailMessage(to, subject, html, WebUtility.HtmlDecode(text)), cancellationToken);
    }

    private string Link(string pathAndQuery) => urls.Value.PublicBaseUrl.TrimEnd('/') + pathAndQuery;

    private void SetCurrentPrincipal(ClaimsPrincipal principal)
    {
        if (httpContextAccessor.HttpContext is { } context)
        {
            context.User = principal;
        }
    }

    private async Task<ApplicationUser> FindRequiredAsync(Guid userId) =>
        await users.FindByIdAsync(userId.ToString())
        ?? throw new UnauthorizedException("AUTHENTICATION_REQUIRED", "You need to sign in to continue.");

    private async Task<UserDto> ToDtoAsync(ApplicationUser user)
    {
        var twoFactorEnabled = await users.GetTwoFactorEnabledAsync(user);
        return new UserDto(
            user.Id,
            user.Email!,
            user.FirstName,
            user.LastName,
            user.PhoneNumber,
            user.EmailConfirmed,
            [.. await users.GetRolesAsync(user)],
            twoFactorEnabled,
            MustEnableTwoFactor: !twoFactorEnabled && await IsTwoFactorRequiredAsync(user));
    }

    private static string EncodeToken(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    private static string? DecodeToken(string token)
    {
        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static void ThrowIfFailed(IdentityResult result, string field)
    {
        if (!result.Succeeded)
        {
            throw new RequestValidationException(new Dictionary<string, string[]>
            {
                [field] = result.Errors.Select(e => e.Description).ToArray(),
            });
        }
    }

    private static UnauthorizedException InvalidCredentials() => new("INVALID_CREDENTIALS", "The email or password is incorrect.");

    private static RequestValidationException InvalidCode() =>
        RequestValidationException.For("code", "That code isn't valid. Check that your phone's clock is correct and try again.");

    private static BusinessRuleException InvalidToken() => new("INVALID_TOKEN", "This link is invalid or has expired. Please request a new one.");
}
