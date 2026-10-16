using Microsoft.AspNetCore.Identity;

namespace SublimaLab.Infrastructure.Identity;

/// <summary>
/// Identity user. Lives in Infrastructure so the domain stays free of ASP.NET Core Identity;
/// domain entities reference customers by <c>UserId</c> only.
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    public ApplicationUser()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
