using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SublimaLab.Application.Common;
using SublimaLab.Domain.Common;

namespace SublimaLab.Api.Http;

/// <summary>
/// Translates exceptions into the standard error body. Expected failures keep their message;
/// unexpected ones are logged with full detail and returned as a generic 500, so stack traces,
/// SQL and internal paths are never exposed.
/// </summary>
internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code, message, errors) = exception switch
        {
            RequestValidationException e => (StatusCodes.Status400BadRequest, e.Code, e.Message, e.Errors),
            UnauthorizedException e => (StatusCodes.Status401Unauthorized, e.Code, e.Message, null),
            ForbiddenException e => (StatusCodes.Status403Forbidden, e.Code, e.Message, null),
            NotFoundException e => (StatusCodes.Status404NotFound, e.Code, e.Message, null),
            ConflictException e => (StatusCodes.Status409Conflict, e.Code, e.Message, null),
            BusinessRuleException e => (StatusCodes.Status422UnprocessableEntity, e.Code, e.Message, null),
            DomainException e => (StatusCodes.Status422UnprocessableEntity, e.Code, e.Message, null),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "CONCURRENCY_CONFLICT", "The resource was changed by someone else. Please reload and try again.", null),
            BadHttpRequestException e => (e.StatusCode, e.StatusCode == StatusCodes.Status413PayloadTooLarge ? "PAYLOAD_TOO_LARGE" : "BAD_REQUEST", e.StatusCode == StatusCodes.Status413PayloadTooLarge ? "The request is too large." : "The request is invalid.", null),
            InvalidDataException => (StatusCodes.Status400BadRequest, "BAD_REQUEST", "The request body could not be read.", null),
            OperationCanceledException when context.RequestAborted.IsCancellationRequested => (499, "REQUEST_CANCELLED", "The request was cancelled.", null),
            _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR", "Something went wrong on our side. Please try again later.", (IReadOnlyDictionary<string, string[]>?)null),
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        else if (status != 499)
        {
            logger.LogInformation("Request {Method} {Path} failed with {Status} {Code}", context.Request.Method, context.Request.Path, status, code);
        }

        await ApiErrors.WriteAsync(context, status, code, message, errors);
        return true;
    }
}
