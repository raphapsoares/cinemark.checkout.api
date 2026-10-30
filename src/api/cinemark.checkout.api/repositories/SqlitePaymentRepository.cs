using cinemark.checkout.api.interfaces;
using cinemark.checkout.api.models;
using cinemark.checkout.shared.data;
using Microsoft.Data.Sqlite;

namespace cinemark.checkout.api.repositories;

/// <summary>
/// Implementação ADO.NET de <see cref="IPaymentRepository"/>. Só mexe na tabela payments.
/// </summary>
public sealed class SqlitePaymentRepository : IPaymentRepository
{
    public async Task SaveAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PaymentRecord payment,
        CancellationToken cancellationToken)
    {
        await using var insert = connection.CreateCommand(
                """
                INSERT INTO payments (id, order_id, idempotency_key, amount, status, provider_ref, attempts, updated_at)
                VALUES (@id, @order_id, @idempotency_key, @amount, @status, @provider_ref, @attempts, @updated_at)
                """,
                transaction)
            .WithParameter("@id", payment.Id.ToString())
            .WithParameter("@order_id", payment.OrderId.ToString())
            .WithParameter("@idempotency_key", payment.IdempotencyKey)
            .WithParameter("@amount", payment.Amount.ToMoneyText())
            .WithParameter("@status", (int)payment.Status)
            .WithParameter("@provider_ref", payment.ProviderReference)
            .WithParameter("@attempts", payment.Attempts)
            .WithParameter("@updated_at", payment.UpdatedAt.ToIsoUtc());

        await insert.ExecuteNonQueryAsync(cancellationToken);
    }
}
