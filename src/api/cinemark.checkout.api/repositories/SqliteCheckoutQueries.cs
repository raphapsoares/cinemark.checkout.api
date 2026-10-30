using cinemark.checkout.api.interfaces;
using cinemark.checkout.api.models;
using cinemark.checkout.shared.data;
using cinemark.checkout.shared.enums;
using cinemark.checkout.shared.interfaces;
using Microsoft.Data.Sqlite;

namespace cinemark.checkout.api.repositories;

/// <summary>
/// Implementação ADO.NET de <see cref="ICheckoutQueries"/>. Só lê: junta pedido, itens, reservas e pagamento
/// para os GETs da API, sem transação.
/// </summary>
public sealed class SqliteCheckoutQueries : ICheckoutQueries
{
    private readonly IDbConnectionFactory _connections;

    public SqliteCheckoutQueries(IDbConnectionFactory connections)
    {
        _connections = connections;
    }

    public async Task<OrderDetails?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var id = orderId.ToString();

        OrderRecord order;
        await using (var query = connection.CreateCommand(
                         $"SELECT {SqliteOrderRepository.SelectColumns} FROM orders WHERE id = @id")
                         .WithParameter("@id", id))
        await using (var reader = await query.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            order = SqliteOrderRepository.ReadOrder(reader);
        }

        var items = await ReadAllAsync(
            connection,
            "SELECT product_id, quantity, unit_price FROM order_items WHERE order_id = @id ORDER BY product_id",
            id,
            reader => new OrderItemRecord(reader.GetString(0), reader.GetInt32(1), SqliteExtensions.ParseMoney(reader.GetString(2))),
            cancellationToken);

        var reservations = await ReadAllAsync(
            connection,
            "SELECT product_id, quantity, status, expires_at FROM reservations WHERE order_id = @id ORDER BY product_id",
            id,
            reader => new ReservationRecord(
                reader.GetString(0),
                reader.GetInt32(1),
                SqliteExtensions.ToEnum<ReservationStatus>(reader.GetInt32(2)),
                SqliteExtensions.ParseIsoUtc(reader.GetString(3))),
            cancellationToken);

        var payments = await ReadAllAsync(
            connection,
            "SELECT id, idempotency_key, amount, status, provider_ref, attempts, updated_at FROM payments WHERE order_id = @id",
            id,
            reader => new PaymentRecord(
                Guid.Parse(reader.GetString(0)),
                orderId,
                reader.GetString(1),
                SqliteExtensions.ParseMoney(reader.GetString(2)),
                SqliteExtensions.ToEnum<PaymentStatus>(reader.GetInt32(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetInt32(5),
                SqliteExtensions.ParseIsoUtc(reader.GetString(6))),
            cancellationToken);

        return new OrderDetails(order, items, reservations, payments.SingleOrDefault());
    }

    public async Task<ProductRecord?> GetProductAsync(string productId, CancellationToken cancellationToken)
    {
        await using var connection = await _connections.OpenConnectionAsync(cancellationToken);
        var products = await ReadAllAsync(
            connection,
            "SELECT id, name, available_quantity, unit_price FROM products WHERE id = @id",
            productId,
            reader => new ProductRecord(
                reader.GetString(0), reader.GetString(1), reader.GetInt32(2), SqliteExtensions.ParseMoney(reader.GetString(3))),
            cancellationToken);

        return products.SingleOrDefault();
    }

    private static async Task<IReadOnlyList<T>> ReadAllAsync<T>(
        SqliteConnection connection,
        string sql,
        string id,
        Func<SqliteDataReader, T> map,
        CancellationToken cancellationToken)
    {
        await using var query = connection.CreateCommand(sql).WithParameter("@id", id);
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);

        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(map(reader));
        }

        return rows;
    }
}
