using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;
using SublimaLab.Infrastructure;
using SublimaLab.Infrastructure.Identity;

namespace SublimaLab.Api.Http;

/// <summary>The session must have been signed in with a second factor (when the setting requires it).</summary>
internal sealed class MultiFactorRequirement : IAuthorizationRequirement;

internal sealed class MultiFactorRequirementHandler(IOptionsMonitor<AuthOptions> options) : AuthorizationHandler<MultiFactorRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, MultiFactorRequirement requirement)
    {
        if (!options.CurrentValue.RequireAdminTwoFactor || context.User.HasClaim(MultiFactor.ClaimType, MultiFactor.ClaimValue))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Gives a missing second factor its own error code, so clients can send the user to set up
/// two-step verification instead of showing a generic "forbidden".
/// </summary>
internal sealed class MultiFactorAwareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && authorizeResult.AuthorizationFailure?.FailedRequirements.OfType<MultiFactorRequirement>().Any() == true &&
            !authorizeResult.AuthorizationFailure.FailedRequirements.OfType<Microsoft.AspNetCore.Authorization.Infrastructure.RolesAuthorizationRequirement>().Any())
        {
            return ApiErrors.WriteAsync(context, StatusCodes.Status403Forbidden, "TWO_FACTOR_REQUIRED",
                "Administrators must sign in with two-step verification. Set it up under My account → Profile & security.");
        }

        return _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
