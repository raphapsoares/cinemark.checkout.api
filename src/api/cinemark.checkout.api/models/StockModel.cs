using cinemark.checkout.shared.enums;

namespace cinemark.checkout.api.models;

// Estoque: produto do catálogo, reserva de um pedido e o ajuste feito pelo endpoint de demonstração.

public sealed record ProductRecord(string Id, string Name, int AvailableQuantity, decimal UnitPrice);

public sealed record ReservationRecord(string ProductId, int Quantity, ReservationStatus Status, DateTimeOffset ExpiresAt);

/// <summary>Corpo do PUT /demo/products/{id}. Campo ausente não muda.</summary>
public sealed record UpdateProductRequest(int? AvailableQuantity, decimal? UnitPrice);
