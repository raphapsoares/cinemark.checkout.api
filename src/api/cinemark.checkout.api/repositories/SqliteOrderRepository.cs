using cinemark.checkout.api.interfaces;
using cinemark.checkout.api.models;
using cinemark.checkout.shared.data;
using cinemark.checkout.shared.enums;
using Microsoft.Data.Sqlite;

namespace cinemark.checkout.api.repositories;

/// <summary>
/// Implementação ADO.NET de <see cref="IOrderRepository"/>. Só mexe em orders e order_items.
/// </summary>
public sealed class SqliteOrderRepository : IOrderRepository
{
    internal const string SelectColumns =
        "id, idempotency_key, customer_id, status, total_amount, failure_reason, created_at, updated_at";

    private readonly TimeProvider _timeProvider;

    public SqliteOrderRepository(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public async Task<(OrderRecord Order, bool Created)> TryCreatePendingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OrderRecord order,
        IReadOnlyList<OrderItemRecord> items,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var insertOrder = connection.CreateCommand(
                    """
                    INSERT INTO orders (id, idempotency_key, customer_id, status, total_amount, failure_reason, created_at, updated_at)
                    VALUES (@id, @idempotency_key, @customer_id, @status, @total_amount, NULL, @created_at, @updated_at)
                    """,
                    transaction)
                .WithParameter("@id", order.Id.ToString())
                .WithParameter("@idempotency_key", order.IdempotencyKey)
                .WithParameter("@customer_id", order.CustomerId)
                .WithParameter("@status", (int)order.Status)
                .WithParameter("@total_amount", order.TotalAmount.ToMoneyText())
                .WithParameter("@created_at", order.CreatedAt.ToIsoUtc())
                .WithParameter("@updated_at", order.UpdatedAt.ToIsoUtc());

            await insertOrder.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException ex) when (ex.IsUniqueViolation())
        {
            // Só o INSERT falhou (o SQLite desfaz apenas o comando); a transação continua válida para a leitura.
            var existing = await GetByIdempotencyKeyAsync(connection, transaction, order.IdempotencyKey, cancellationToken)
                           ?? throw new InvalidOperationException(
                               $"Idempotency key '{order.IdempotencyKey}' conflicted but no order was found.", ex);

            return (existing, false);
        }

        foreach (var item in items)
        {
            await using var insertItem = connection.CreateCommand(
                    """
                    INSERT INTO order_items (id, order_id, product_id, quantity, unit_price)
                    VALUES (@id, @order_id, @product_id, @quantity, @unit_price)
                    """,
                    transaction)
                .WithParameter("@id", Guid.NewGuid().ToString())
                .WithParameter("@order_id", order.Id.ToString())
                .WithParameter("@product_id", item.ProductId)
                .WithParameter("@quantity", item.Quantity)
                .WithParameter("@unit_price", item.UnitPrice.ToMoneyText());

            await insertItem.ExecuteNonQueryAsync(cancellationToken);
        }

        return (order, true);
    }

    public async Task<bool> TryTransitionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid orderId,
        OrderStatus expected,
        OrderStatus next,
        string? failureReason,
        CancellationToken cancellationToken)
    {
        await using var update = connection.CreateCommand(
                """
                UPDATE orders
                SET status = @next, failure_reason = @failure_reason, updated_at = @updated_at
                WHERE id = @id AND status = @expected
                """,
                transaction)
            .WithParameter("@next", (int)next)
            .WithParameter("@failure_reason", failureReason)
            .WithParameter("@updated_at", _timeProvider.GetUtcNow().ToIsoUtc())
            .WithParameter("@id", orderId.ToString())
            .WithParameter("@expected", (int)expected);

        return await update.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task<OrderRecord?> GetByIdempotencyKeyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var query = connection.CreateCommand(
                $"SELECT {SelectColumns} FROM orders WHERE idempotency_key = @idempotency_key", transaction)
            .WithParameter("@idempotency_key", idempotencyKey);

        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadOrder(reader) : null;
    }

    /// <summary>Lê uma linha selecionada com <see cref="SelectColumns"/>.</summary>
    internal static OrderRecord ReadOrder(SqliteDataReader reader) =>
        new(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            SqliteExtensions.ToEnum<OrderStatus>(reader.GetInt32(3)),
            SqliteExtensions.ParseMoney(reader.GetString(4)),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            SqliteExtensions.ParseIsoUtc(reader.GetString(6)),
            SqliteExtensions.ParseIsoUtc(reader.GetString(7)));
}
