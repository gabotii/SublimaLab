namespace SublimaLab.Domain.Common;

/// <summary>Timestamps maintained automatically by the persistence layer (UTC).</summary>
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}
