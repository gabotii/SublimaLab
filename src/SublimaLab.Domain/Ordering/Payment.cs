using SublimaLab.Domain.Common;

namespace SublimaLab.Domain.Ordering;

public enum PaymentStatus
{
    Pending = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
    Refunded = 5,
}

/// <summary>
/// A payment attempt with an external provider. Only the provider's reference is stored;
/// card data never touches SublimaLab servers.
/// </summary>
public sealed class Payment : Entity
{
    private Payment() { }

    internal Payment(Guid orderId, string provider, string providerPaymentId, decimal amount, string currency, DateTime now)
    {
        OrderId = orderId;
        Provider = provider;
        ProviderPaymentId = providerPaymentId;
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid OrderId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string ProviderPaymentId { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public PaymentStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    internal void MarkSucceeded(DateTime now) => Transition(PaymentStatus.Succeeded, null, now);

    internal void MarkFailed(string reason, DateTime now) => Transition(PaymentStatus.Failed, reason, now);

    internal void MarkCancelled(DateTime now) => Transition(PaymentStatus.Cancelled, null, now);

    private void Transition(PaymentStatus status, string? reason, DateTime now)
    {
        if (Status != PaymentStatus.Pending)
        {
            throw new DomainException("PAYMENT_ALREADY_FINALIZED", $"The payment is already '{Status}'.");
        }

        Status = status;
        FailureReason = reason;
        UpdatedAt = now;
    }
}
