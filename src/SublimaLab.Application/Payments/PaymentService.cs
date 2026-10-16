using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SublimaLab.Application.Abstractions;
using SublimaLab.Application.Common;
using SublimaLab.Application.Orders;
using SublimaLab.Domain.Ordering;

namespace SublimaLab.Application.Payments;

public sealed record CreatePaymentRequest(Guid OrderId);

/// <param name="PaymentToken">Token produced by the provider's client SDK. The test gateway accepts "tok_test_success" or "tok_test_declined".</param>
public sealed record ConfirmPaymentRequest(string? PaymentToken);

public sealed record PaymentOutcomeDto(PaymentDto Payment, OrderStatus OrderStatus, string OrderNumber);

public sealed class ConfirmPaymentRequestValidator : AbstractValidator<ConfirmPaymentRequest>
{
    public ConfirmPaymentRequestValidator()
    {
        RuleFor(r => r.PaymentToken).MaximumLength(200);
    }
}

/// <summary>
/// Orchestrates payments through <see cref="IPaymentGateway"/>. The amount charged always
/// comes from the persisted order total, and the provider's reported amount is verified.
/// </summary>
public sealed class PaymentService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IPaymentGateway gateway,
    IValidator<ConfirmPaymentRequest> confirmValidator,
    IEmailSender emailSender,
    IOptions<AppUrlOptions> urls,
    TimeProvider clock,
    ILogger<PaymentService> logger)
{
    public async Task<PaymentDto> CreateAsync(CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        var order = await db.Orders
            .Include(o => o.Payments)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId && o.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Order");

        if (order.Status is not (OrderStatus.Pending or OrderStatus.PaymentPending))
        {
            throw new BusinessRuleException("ORDER_NOT_PAYABLE", "This order cannot be paid.");
        }

        var result = await gateway.CreatePaymentAsync(order, cancellationToken);
        if (result.Status == GatewayPaymentStatus.Failed)
        {
            throw new BusinessRuleException("PAYMENT_PROVIDER_ERROR", result.FailureReason ?? "The payment could not be started. Please try again.");
        }

        var payment = order.StartPayment(gateway.ProviderName, result.ProviderPaymentId, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Payment {PaymentId} started for order {OrderNumber} via {Provider}", payment.Id, order.OrderNumber, gateway.ProviderName);
        return payment.ToDto(result.ClientSecret);
    }

    public async Task<PaymentOutcomeDto> ConfirmAsync(Guid paymentId, ConfirmPaymentRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireUserId();
        await confirmValidator.ValidateOrThrowAsync(request, cancellationToken);

        var order = await db.Orders
            .Include(o => o.Payments)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.UserId == userId && o.Payments.Any(p => p.Id == paymentId), cancellationToken)
            ?? throw new NotFoundException("Payment");

        var payment = order.Payments.First(p => p.Id == paymentId);
        if (payment.Status != PaymentStatus.Pending)
        {
            throw new ConflictException("PAYMENT_ALREADY_FINALIZED", "This payment has already been processed.");
        }

        var result = await gateway.ConfirmPaymentAsync(payment.ProviderPaymentId, request.PaymentToken, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;

        if (result.Status == GatewayPaymentStatus.Succeeded && (result.Amount != payment.Amount || !string.Equals(result.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase)))
        {
            logger.LogError("Payment {PaymentId} amount mismatch: expected {Expected} {Currency}, provider reported {Actual} {ActualCurrency}",
                payment.Id, payment.Amount, payment.Currency, result.Amount, result.Currency);
            order.FailPayment(payment.Id, "Payment amount verification failed.", now);
        }
        else if (result.Status == GatewayPaymentStatus.Succeeded)
        {
            order.ConfirmPayment(payment.Id, now);
            logger.LogInformation("Payment {PaymentId} succeeded; order {OrderNumber} is paid", payment.Id, order.OrderNumber);
            StoreMetrics.Payments.Add(1, new("provider", gateway.ProviderName), new("outcome", "succeeded"));
        }
        else
        {
            order.FailPayment(payment.Id, result.FailureReason ?? "The payment was declined.", now);
            logger.LogInformation("Payment {PaymentId} for order {OrderNumber} failed: {Reason}", payment.Id, order.OrderNumber, result.FailureReason);
            StoreMetrics.Payments.Add(1, new("provider", gateway.ProviderName), new("outcome", "failed"));
        }

        await db.SaveChangesAsync(cancellationToken);

        if (order.Status == OrderStatus.Paid)
        {
            await SendConfirmationAsync(order, cancellationToken);
        }

        return new PaymentOutcomeDto(payment.ToDto(), order.Status, order.OrderNumber);
    }

    /// <summary>The payment is already committed: an email problem is logged, never surfaced as a failure.</summary>
    private async Task SendConfirmationAsync(Order order, CancellationToken cancellationToken)
    {
        try
        {
            var withItems = await db.Orders.AsNoTracking().Include(o => o.Items).FirstAsync(o => o.Id == order.Id, cancellationToken);
            await emailSender.SendAsync(OrderConfirmationEmail.Create(withItems, urls.Value.PublicBaseUrl), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not send the confirmation email for order {OrderNumber}", order.OrderNumber);
        }
    }
}
