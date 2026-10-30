using Microsoft.Data.Sqlite;

namespace cinemark.checkout.shared.interfaces;

/// <summary>
/// Creates and opens database connections. The caller owns and disposes the connection.
/// </summary>
public interface IDbConnectionFactory
{
    Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken);
}
