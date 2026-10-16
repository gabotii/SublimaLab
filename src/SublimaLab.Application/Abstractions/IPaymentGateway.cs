using SublimaLab.Domain.Ordering;

namespace SublimaLab.Application.Abstractions;

public enum GatewayPaymentStatus
{
    /// <summary>Created at the provider; the customer still has to confirm it.</summary>
    RequiresConfirmation,
    Succeeded,
    Failed,
}

/// <param name="ClientSecret">Opaque value some providers need client-side (e.g. Stripe Payment Element).</param>
/// <param name="Amount">Amount the provider reports for the payment, used to verify totals.</param>
public sealed record PaymentResult(
    GatewayPaymentStatus Status,
    string ProviderPaymentId,
    decimal Amount,
    string Currency,
    string? ClientSecret = null,
    string? FailureReason = null);

/// <summary>
/// Payment provider abstraction. Card data is collected by the provider (hosted fields or
/// redirect) so SublimaLab only ever handles provider references and tokens.
/// </summary>
public interface IPaymentGateway
{
    string ProviderName { get; }

    Task<PaymentResult> CreatePaymentAsync(Order order, CancellationToken cancellationToken);

    /// <param name="paymentToken">Provider token produced client-side (e.g. a payment method id).</param>
    Task<PaymentResult> ConfirmPaymentAsync(string providerPaymentId, string? paymentToken, CancellationToken cancellationToken);
}
