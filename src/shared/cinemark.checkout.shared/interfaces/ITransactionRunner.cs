using Microsoft.Data.Sqlite;

namespace cinemark.checkout.shared.interfaces;

/// <summary>
/// Executa um trabalho dentro de uma única transação: commit se terminar bem, rollback se lançar exceção.
/// Usado no checkout para gravar pedido e reserva (e depois pagamento e confirmação) de forma atômica.
/// </summary>
public interface ITransactionRunner
{
    Task<T> RunAsync<T>(
        Func<SqliteConnection, SqliteTransaction, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken);
}
