using SublimaLab.Domain.Common;

namespace SublimaLab.Domain.Customers;

/// <summary>An address saved to a customer's address book.</summary>
public sealed class CustomerAddress : Entity, IAuditable
{
    public Guid UserId { get; set; }
    public string Label { get; set; } = string.Empty;
    public required string RecipientName { get; set; }
    public required string Phone { get; set; }
    public required string Line1 { get; set; }
    public string? Line2 { get; set; }
    public required string City { get; set; }
    public required string Region { get; set; }
    public required string PostalCode { get; set; }
    public required string Country { get; set; }
    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
