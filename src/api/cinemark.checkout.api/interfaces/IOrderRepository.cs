using cinemark.checkout.api.models;
using cinemark.checkout.shared.enums;
using Microsoft.Data.Sqlite;

namespace cinemark.checkout.api.interfaces;

public interface IOrderRepository
{
    /// <summary>
    /// Grava o pedido e os itens. Se a chave de idempotência já existir, não grava nada e devolve o pedido existente
    /// com <c>Created = false</c>.
    /// </summary>
    Task<(OrderRecord Order, bool Created)> TryCreatePendingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OrderRecord order,
        IReadOnlyList<OrderItemRecord> items,
        CancellationToken cancellationToken);

    /// <summary>Muda o status só se o pedido ainda estiver no status esperado.</summary>
    Task<bool> TryTransitionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid orderId,
        OrderStatus expected,
        OrderStatus next,
        string? failureReason,
        CancellationToken cancellationToken);
}
