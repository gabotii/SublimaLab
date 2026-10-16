using System.Globalization;
using System.Net;
using System.Text;
using SublimaLab.Application.Abstractions;
using SublimaLab.Domain.Ordering;

namespace SublimaLab.Application.Orders;

/// <summary>Builds the "order confirmed" email sent once an order is paid.</summary>
internal static class OrderConfirmationEmail
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("en-US");

    public static EmailMessage Create(Order order, string publicBaseUrl)
    {
        var link = $"{publicBaseUrl.TrimEnd('/')}/account/orders/{order.Id}";
        var rows = new StringBuilder();
        var text = new StringBuilder();

        foreach (var item in order.Items.OrderBy(i => i.Id))
        {
            var customized = item.Design is null ? string.Empty : " · Custom design";
            rows.Append(CultureInfo.InvariantCulture,
                $"<tr><td style=\"padding:6px 0\">{item.Quantity} × {Encode(item.ProductName)} <span style=\"color:#6b7080\">({Encode(item.VariantLabel)}{customized})</span></td>" +
                $"<td style=\"padding:6px 0;text-align:right\">{Money(item.LineTotal, order.Currency)}</td></tr>");
            text.AppendLine(CultureInfo.InvariantCulture, $"- {item.Quantity} x {item.ProductName} ({item.VariantLabel}{customized}): {Money(item.LineTotal, order.Currency)}");
        }

        var html = $"""
            <!doctype html>
            <html><body style="font-family:Arial,sans-serif;color:#1f2330;line-height:1.5;max-width:560px">
            <h2 style="color:#7c3aed">SublimaLab</h2>
            <h1 style="font-size:22px">Thank you, {Encode(order.Contact.FullName)}! Your order is confirmed.</h1>
            <p>Order <strong>{Encode(order.OrderNumber)}</strong> is paid and heading to production.</p>
            <table style="width:100%;border-collapse:collapse;font-size:14px">{rows}
            <tr><td style="padding-top:12px;border-top:1px solid #e3e1ee"><strong>Total</strong></td>
            <td style="padding-top:12px;border-top:1px solid #e3e1ee;text-align:right"><strong>{Money(order.Total, order.Currency)}</strong></td></tr></table>
            <p>Shipping to {Encode(order.ShippingAddress.Line1)}, {Encode(order.ShippingAddress.City)} via {Encode(order.ShippingMethodName)}.</p>
            <p><a href="{link}" style="color:#6d28d9">Track your order</a></p>
            </body></html>
            """;

        var body = $"""
            Thank you, {order.Contact.FullName}! Your order {order.OrderNumber} is confirmed.

            {text}
            Total: {Money(order.Total, order.Currency)}

            Track your order: {link}
            """;

        return new EmailMessage(order.Contact.Email, $"Order {order.OrderNumber} confirmed", html, body);
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    private static string Money(decimal amount, string currency) =>
        currency == "USD" ? amount.ToString("C", Culture) : $"{amount.ToString("0.00", CultureInfo.InvariantCulture)} {currency}";
}
