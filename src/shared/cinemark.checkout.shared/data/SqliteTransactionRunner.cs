using cinemark.checkout.shared.interfaces;
using Microsoft.Data.Sqlite;

namespace cinemark.checkout.shared.data;

public sealed class SqliteTransactionRunner : ITransactionRunner
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SqliteTransactionRunner(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<T> RunAsync<T>(
        Func<SqliteConnection, SqliteTransaction, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        // Transação não adiada (BEGIN IMMEDIATE): escritores concorrentes são serializados.
        // Sem commit, o Dispose faz o rollback.
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var result = await work(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
