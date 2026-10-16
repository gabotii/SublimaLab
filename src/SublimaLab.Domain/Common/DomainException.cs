namespace SublimaLab.Domain.Common;

/// <summary>
/// Raised when an operation would violate a business invariant (e.g. an invalid order
/// status transition). <see cref="Code"/> is a stable, machine-readable identifier
/// surfaced to API clients.
/// </summary>
public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
