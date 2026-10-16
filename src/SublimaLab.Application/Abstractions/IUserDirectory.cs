namespace SublimaLab.Application.Abstractions;

public sealed record DirectoryUser(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    bool EmailConfirmed,
    bool IsLockedOut,
    DateTime CreatedAt,
    IReadOnlyList<string> Roles);

/// <summary>Read and administer customer accounts (implemented with ASP.NET Core Identity).</summary>
public interface IUserDirectory
{
    Task<(IReadOnlyList<DirectoryUser> Users, int Total)> SearchAsync(string? search, int page, int pageSize, CancellationToken cancellationToken);

    Task<DirectoryUser?> FindAsync(Guid userId, CancellationToken cancellationToken);

    Task<int> CountCustomersAsync(CancellationToken cancellationToken);

    /// <summary>Locks the account indefinitely (and signs it out everywhere) or unlocks it.</summary>
    Task SetLockedAsync(Guid userId, bool locked, CancellationToken cancellationToken);
}
