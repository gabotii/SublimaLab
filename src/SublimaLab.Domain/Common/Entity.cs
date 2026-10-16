namespace SublimaLab.Domain.Common;

/// <summary>
/// Base type for all entities. Ids are UUIDv7 so they are globally unique, safe to expose
/// in URLs and roughly time-ordered for index locality.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
}
