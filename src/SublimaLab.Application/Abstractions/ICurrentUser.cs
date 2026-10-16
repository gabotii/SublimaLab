using SublimaLab.Application.Common;

namespace SublimaLab.Application.Abstractions;

/// <summary>The authenticated principal of the current request, if any.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
    bool IsAdmin { get; }

    Guid RequireUserId() => UserId ?? throw new UnauthorizedException("AUTHENTICATION_REQUIRED", "You need to sign in to continue.");
}

/// <summary>
/// An anonymous browser session (HttpOnly cookie) that lets guests build a cart and upload
/// artwork before creating an account. Its data is merged into the account on sign-in.
/// </summary>
public interface IClientSession
{
    string? SessionId { get; }

    string GetOrCreateSessionId();
}
