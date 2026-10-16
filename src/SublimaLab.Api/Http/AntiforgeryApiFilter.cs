using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace SublimaLab.Api.Http;

/// <summary>
/// CSRF protection for cookie-authenticated APIs: every unsafe request (POST/PUT/PATCH/DELETE)
/// must echo the antiforgery token in the <c>X-XSRF-TOKEN</c> header. Clients obtain the token
/// from <c>GET /api/antiforgery/token</c> and receive a fresh one after sign-in/sign-out.
/// Opt out per action with <see cref="IgnoreAntiforgeryTokenAttribute"/> (e.g. provider webhooks).
/// </summary>
internal sealed class AntiforgeryApiFilter(IAntiforgery antiforgery, ILogger<AntiforgeryApiFilter> logger) : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
        {
            return;
        }

        var policy = context.FindEffectivePolicy<IAntiforgeryPolicy>();
        if (policy is IgnoreAntiforgeryTokenAttribute)
        {
            return;
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException exception)
        {
            logger.LogInformation("Antiforgery validation failed for {Method} {Path}: {Reason}", method, context.HttpContext.Request.Path, exception.Message);
            context.Result = ApiErrors.Result(context.HttpContext, StatusCodes.Status400BadRequest, "CSRF_TOKEN_INVALID",
                "Your session token is missing or expired. Refresh the page and try again.");
        }
    }
}
