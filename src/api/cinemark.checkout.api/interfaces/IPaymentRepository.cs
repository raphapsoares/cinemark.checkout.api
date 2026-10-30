using cinemark.checkout.api.models;
using Microsoft.Data.Sqlite;

namespace cinemark.checkout.api.interfaces;

public interface IPaymentRepository
{
    /// <summary>Grava o resultado da cobrança, na transação que confirma ou cancela o pedido.</summary>
    Task SaveAsync(SqliteConnection connection, SqliteTransaction transaction, PaymentRecord payment, CancellationToken cancellationToken);
}
