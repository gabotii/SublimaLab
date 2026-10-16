using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SublimaLab.Api.Http;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Accounts;

namespace SublimaLab.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/account")]
public sealed class AccountController(IIdentityService identity, AccountService accounts, ICurrentUser currentUser) : ControllerBase
{
    [HttpGet("profile")]
    public async Task<ActionResult<UserDto>> GetProfile(CancellationToken cancellationToken) =>
        Ok(await identity.GetUserAsync(currentUser.RequireUserId(), cancellationToken));

    [HttpPut("profile")]
    public async Task<ActionResult<UserDto>> UpdateProfile(UpdateProfileRequest request, CancellationToken cancellationToken) =>
        Ok(await identity.UpdateProfileAsync(currentUser.RequireUserId(), request, cancellationToken));

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        await identity.ChangePasswordAsync(currentUser.RequireUserId(), request, cancellationToken);
        return NoContent();
    }

    [HttpGet("two-factor")]
    public async Task<ActionResult<TwoFactorStatusDto>> GetTwoFactor(CancellationToken cancellationToken) =>
        Ok(await identity.GetTwoFactorStatusAsync(currentUser.RequireUserId(), cancellationToken));

    /// <summary>Starts (or restarts) authenticator setup and returns the key and QR code.</summary>
    [HttpPost("two-factor/setup")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<TwoFactorSetupDto>> SetupTwoFactor(CancellationToken cancellationToken) =>
        Ok(await identity.BeginTwoFactorSetupAsync(currentUser.RequireUserId(), cancellationToken));

    [HttpPost("two-factor/enable")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<RecoveryCodesDto>> EnableTwoFactor(TwoFactorCodeRequest request, CancellationToken cancellationToken) =>
        Ok(await identity.EnableTwoFactorAsync(currentUser.RequireUserId(), request, cancellationToken));

    [HttpPost("two-factor/disable")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    public async Task<IActionResult> DisableTwoFactor(TwoFactorCodeRequest request, CancellationToken cancellationToken)
    {
        await identity.DisableTwoFactorAsync(currentUser.RequireUserId(), request, cancellationToken);
        return NoContent();
    }

    [HttpPost("two-factor/recovery-codes")]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<RecoveryCodesDto>> RegenerateRecoveryCodes(TwoFactorCodeRequest request, CancellationToken cancellationToken) =>
        Ok(await identity.RegenerateRecoveryCodesAsync(currentUser.RequireUserId(), request, cancellationToken));

    [HttpGet("addresses")]
    public async Task<ActionResult<IReadOnlyList<CustomerAddressDto>>> GetAddresses(CancellationToken cancellationToken) =>
        Ok(await accounts.GetAddressesAsync(cancellationToken));

    [HttpPost("addresses")]
    public async Task<ActionResult<CustomerAddressDto>> CreateAddress(SaveAddressRequest request, CancellationToken cancellationToken)
    {
        var address = await accounts.CreateAddressAsync(request, cancellationToken);
        return Created($"/api/account/addresses/{address.Id}", address);
    }

    [HttpPut("addresses/{id:guid}")]
    public async Task<ActionResult<CustomerAddressDto>> UpdateAddress(Guid id, SaveAddressRequest request, CancellationToken cancellationToken) =>
        Ok(await accounts.UpdateAddressAsync(id, request, cancellationToken));

    [HttpDelete("addresses/{id:guid}")]
    public async Task<IActionResult> DeleteAddress(Guid id, CancellationToken cancellationToken)
    {
        await accounts.DeleteAddressAsync(id, cancellationToken);
        return NoContent();
    }
}
