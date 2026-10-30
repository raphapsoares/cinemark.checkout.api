using System.Net;
using cinemark.checkout.shared.enums;

namespace cinemark.checkout.api.models;

// POST /checkout: o que o cliente envia, o resultado do serviço e a resposta HTTP.

/// <summary>
/// Corpo do POST /checkout. No JSON os campos podem vir faltando; o <see cref="validators.CheckoutRequestValidator"/>
/// confere tudo antes de o serviço usar.
/// </summary>
public sealed record CheckoutRequest(string CustomerId, IReadOnlyList<CheckoutRequestItem> Items);

/// <summary>Item pedido pelo cliente. O preço não vem do cliente: é lido do catálogo (products.unit_price).</summary>
public sealed record CheckoutRequestItem(string ProductId, int Quantity);

public sealed record CheckoutResult(Guid OrderId, OrderStatus Status, string? FailureReason, decimal TotalAmount, bool IsReplay);

/// <summary>Valores de orders.failure_reason.</summary>
public static class FailureReasons
{
    public const string StockInsufficient = "STOCK_INSUFFICIENT";
    public const string PaymentDeclined = "PAYMENT_DECLINED";
    public const string PaymentUnavailable = "PAYMENT_UNAVAILABLE";
}

/// <summary>
/// Corpo da resposta do checkout: { orderId, status, failureReason, totalAmount }.
/// </summary>
public sealed record CheckoutResponse(Guid OrderId, OrderStatus Status, string? FailureReason, decimal TotalAmount)
{
    /// <summary>
    /// 201 confirmado; 200 replay de chave já concluída; 409 chave ainda em andamento;
    /// 422 estoque insuficiente ou pagamento recusado; 503 pagamento indisponível.
    /// </summary>
    public static IResult From(CheckoutResult result)
    {
        var statusCode = result switch
        {
            { IsReplay: true, Status: OrderStatus.Pending } => HttpStatusCode.Conflict,
            { IsReplay: true } => HttpStatusCode.OK,
            { Status: OrderStatus.Confirmed } => HttpStatusCode.Created,
            { FailureReason: FailureReasons.PaymentUnavailable } => HttpStatusCode.ServiceUnavailable,
            _ => HttpStatusCode.UnprocessableEntity,
        };

        return Results.Json(
            new CheckoutResponse(result.OrderId, result.Status, result.FailureReason, result.TotalAmount),
            statusCode: (int)statusCode);
    }
}
