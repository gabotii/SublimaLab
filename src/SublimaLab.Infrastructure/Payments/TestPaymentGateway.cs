using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SublimaLab.Application.Abstractions;
using SublimaLab.Domain.Ordering;

namespace SublimaLab.Infrastructure.Payments;

/// <summary>
/// A provider simulator for development and testing. It behaves like a real two-step provider
/// (create a payment intent, then confirm it with a client token) without moving money.
/// Payment intents live in memory, so a restart between create and confirm makes the customer
/// start a new payment, exactly as an expired intent would at a real provider.
/// </summary>
/// <remarks>
/// Test tokens: <c>tok_test_success</c> (or no token) succeeds; <c>tok_test_declined</c> and
/// <c>tok_test_insufficient_funds</c> fail. Register a real <see cref="IPaymentGateway"/>
/// (e.g. Stripe) in its place for production.
/// </remarks>
public sealed class TestPaymentGateway(ILogger<TestPaymentGateway> logger) : IPaymentGateway
{
    public const string SuccessToken = "tok_test_success";
    public const string DeclinedToken = "tok_test_declined";
    public const string InsufficientFundsToken = "tok_test_insufficient_funds";

    private readonly ConcurrentDictionary<string, (decimal Amount, string Currency)> _intents = new();

    public string ProviderName => "test";

    public Task<PaymentResult> CreatePaymentAsync(Order order, CancellationToken cancellationToken)
    {
        var id = $"test_pi_{Guid.NewGuid():N}";
        _intents[id] = (order.Total, order.Currency);
        logger.LogInformation("Test gateway created intent {PaymentId} for {Amount} {Currency}", id, order.Total, order.Currency);
        return Task.FromResult(new PaymentResult(GatewayPaymentStatus.RequiresConfirmation, id, order.Total, order.Currency, ClientSecret: $"{id}_secret"));
    }

    public Task<PaymentResult> ConfirmPaymentAsync(string providerPaymentId, string? paymentToken, CancellationToken cancellationToken)
    {
        if (!_intents.TryRemove(providerPaymentId, out var intent))
        {
            return Task.FromResult(new PaymentResult(GatewayPaymentStatus.Failed, providerPaymentId, 0m, string.Empty, FailureReason: "The payment session expired. Please try again."));
        }

        var result = paymentToken switch
        {
            null or "" or SuccessToken => new PaymentResult(GatewayPaymentStatus.Succeeded, providerPaymentId, intent.Amount, intent.Currency),
            DeclinedToken => Failed("Your card was declined."),
            InsufficientFundsToken => Failed("Your card has insufficient funds."),
            _ => Failed("The payment details are invalid."),
        };

        return Task.FromResult(result);

        PaymentResult Failed(string reason) => new(GatewayPaymentStatus.Failed, providerPaymentId, intent.Amount, intent.Currency, FailureReason: reason);
    }
}
