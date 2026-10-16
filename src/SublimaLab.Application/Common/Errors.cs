namespace SublimaLab.Application.Common;

/// <summary>
/// Base type for expected, client-facing failures. The API maps each subtype to an HTTP
/// status code and the standard error body; <see cref="Code"/> is stable for clients.
/// </summary>
public abstract class AppException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>400: the request is malformed or fails validation.</summary>
public sealed class RequestValidationException(IReadOnlyDictionary<string, string[]> errors, string message = "One or more fields are invalid.")
    : AppException("VALIDATION_ERROR", message)
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public static RequestValidationException For(string field, string error) =>
        new(new Dictionary<string, string[]> { [field] = [error] });
}

/// <summary>401: the caller is not authenticated or credentials are wrong.</summary>
public sealed class UnauthorizedException(string code, string message) : AppException(code, message);

/// <summary>403: the caller is authenticated but not allowed to perform the operation.</summary>
public sealed class ForbiddenException(string code, string message) : AppException(code, message);

/// <summary>404: the resource does not exist, or the caller may not know it exists.</summary>
public sealed class NotFoundException(string resource) : AppException("NOT_FOUND", $"{resource} was not found.");

/// <summary>409: the request conflicts with the current state of a resource.</summary>
public sealed class ConflictException(string code, string message) : AppException(code, message);

/// <summary>422: the request is well-formed but breaks a business rule.</summary>
public sealed class BusinessRuleException(string code, string message) : AppException(code, message);
