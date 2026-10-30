using cinemark.checkout.api.interfaces;
using cinemark.checkout.api.models;
using cinemark.checkout.shared.data;
using cinemark.checkout.shared.enums;
using Microsoft.Data.Sqlite;

namespace cinemark.checkout.api.repositories;

public sealed class SqliteStockRepository : IStockRepository
{
    private const string ReserveSavepoint = "reserve_stock";

    public async Task<IReadOnlyDictionary<string, decimal>> GetUnitPricesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyCollection<string> productIds,
        CancellationToken cancellationToken)
    {
        var names = productIds.Select((_, index) => $"@p{index}").ToList();
        await using var query = connection.CreateCommand(
            $"SELECT id, unit_price FROM products WHERE id IN ({string.Join(", ", names)})", transaction);
        foreach (var (name, productId) in names.Zip(productIds))
        {
            query.WithParameter(name, productId);
        }

        var prices = new Dictionary<string, decimal>(StringComparer.Ordinal);
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            prices[reader.GetString(0)] = SqliteExtensions.ParseMoney(reader.GetString(1));
        }

        return prices;
    }

    public async Task<bool> TryReserveAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid orderId,
        IReadOnlyCollection<CheckoutRequestItem> items,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        await transaction.SaveAsync(ReserveSavepoint, cancellationToken);

        foreach (var item in items)
        {
            await using var debit = connection.CreateCommand(
                    """
                    UPDATE products
                    SET available_quantity = available_quantity - @quantity
                    WHERE id = @product_id AND available_quantity >= @quantity
                    """,
                    transaction)
                .WithParameter("@quantity", item.Quantity)
                .WithParameter("@product_id", item.ProductId);

            if (await debit.ExecuteNonQueryAsync(cancellationToken) == 0)
            {
                await transaction.RollbackToSavepointAsync(ReserveSavepoint);
                return false;
            }
        }

        foreach (var item in items)
        {
            await using var insert = connection.CreateCommand(
                    """
                    INSERT INTO reservations (id, order_id, product_id, quantity, status, expires_at)
                    VALUES (@id, @order_id, @product_id, @quantity, @status, @expires_at)
                    """,
                    transaction)
                .WithParameter("@id", Guid.NewGuid().ToString())
                .WithParameter("@order_id", orderId.ToString())
                .WithParameter("@product_id", item.ProductId)
                .WithParameter("@quantity", item.Quantity)
                .WithParameter("@status", (int)ReservationStatus.Reserved)
                .WithParameter("@expires_at", expiresAt.ToIsoUtc());

            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.ReleaseAsync(ReserveSavepoint, cancellationToken);
        return true;
    }

    public async Task ReleaseAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        await using (var restock = connection.CreateCommand(
                             """
                             UPDATE products
                             SET available_quantity = available_quantity + (
                                 SELECT SUM(r.quantity)
                                 FROM reservations r
                                 WHERE r.order_id = @order_id AND r.product_id = products.id AND r.status = @reserved)
                             WHERE id IN (
                                 SELECT product_id FROM reservations WHERE order_id = @order_id AND status = @reserved)
                             """,
                             transaction)
                         .WithParameter("@order_id", orderId.ToString())
                         .WithParameter("@reserved", (int)ReservationStatus.Reserved))
        {
            await restock.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var release = connection.CreateCommand(
                "UPDATE reservations SET status = @released WHERE order_id = @order_id AND status = @reserved",
                transaction)
            .WithParameter("@released", (int)ReservationStatus.Released)
            .WithParameter("@order_id", orderId.ToString())
            .WithParameter("@reserved", (int)ReservationStatus.Reserved);

        await release.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ConfirmAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        await using var confirm = connection.CreateCommand(
                "UPDATE reservations SET status = @confirmed WHERE order_id = @order_id AND status = @reserved",
                transaction)
            .WithParameter("@confirmed", (int)ReservationStatus.Confirmed)
            .WithParameter("@order_id", orderId.ToString())
            .WithParameter("@reserved", (int)ReservationStatus.Reserved);

        await confirm.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> UpdateProductAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string productId,
        int? availableQuantity,
        decimal? unitPrice,
        CancellationToken cancellationToken)
    {
        await using var update = connection.CreateCommand(
                """
                UPDATE products
                SET available_quantity = COALESCE(@quantity, available_quantity),
                    unit_price = COALESCE(@price, unit_price)
                WHERE id = @product_id
                """,
                transaction)
            .WithParameter("@quantity", availableQuantity)
            .WithParameter("@price", unitPrice?.ToMoneyText())
            .WithParameter("@product_id", productId);

        return await update.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
