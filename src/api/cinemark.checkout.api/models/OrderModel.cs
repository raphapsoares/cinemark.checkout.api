using cinemark.checkout.shared.enums;

namespace cinemark.checkout.api.models;

// Pedido gravado (orders e order_items) e a visão completa devolvida pelo GET /orders/{id}.

public sealed record OrderRecord(
    Guid Id,
    string IdempotencyKey,
    string CustomerId,
    OrderStatus Status,
    decimal TotalAmount,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Item gravado no pedido, com o preço do catálogo no momento da compra.</summary>
public sealed record OrderItemRecord(string ProductId, int Quantity, decimal UnitPrice);

/// <summary>Resposta do GET /orders/{id}: o pedido com itens, reservas e pagamento (null se não houve cobrança).</summary>
public sealed record OrderDetails(
    OrderRecord Order,
    IReadOnlyList<OrderItemRecord> Items,
    IReadOnlyList<ReservationRecord> Reservations,
    PaymentRecord? Payment);
