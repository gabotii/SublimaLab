namespace SublimaLab.Infrastructure.Identity;

/// <summary>
/// The claim ASP.NET Core Identity adds when a session was signed in with a second factor
/// (RFC 8176 "amr" = "mfa").
/// </summary>
public static class MultiFactor
{
    public const string ClaimType = "amr";
    public const string ClaimValue = "mfa";
}
