using System.Globalization;
using cinemark.checkout.shared.interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace cinemark.checkout.shared.data;

/// <summary>
/// Opens connections to an existing SQLite file (Mode=ReadWrite, so a missing file is never created),
/// with foreign keys enabled and a busy timeout.
/// </summary>
public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    private const int SqliteCantOpen = 14;

    private readonly DatabaseOptions _options;
    private readonly string _connectionString;

    public SqliteConnectionFactory(IOptions<DatabaseOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.DatabasePath))
        {
            throw new InvalidOperationException(
                $"Database path is not configured. Set {DatabaseOptions.SectionName}:DatabasePath in appsettings.json " +
                $"or the {DatabaseOptions.SectionName}__DatabasePath environment variable.");
        }

        _connectionString = BuildConnectionString(_options);
    }

    public static string BuildConnectionString(DatabaseOptions options) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = options.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = true,
        }.ToString();

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);

            await using var pragma = connection.CreateCommand();
            pragma.CommandText = string.Create(
                CultureInfo.InvariantCulture,
                $"PRAGMA foreign_keys = ON; PRAGMA busy_timeout = {_options.BusyTimeoutMs};");
            await pragma.ExecuteNonQueryAsync(cancellationToken);

            return connection;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == SqliteCantOpen)
        {
            await connection.DisposeAsync();
            throw new InvalidOperationException(
                $"Could not open the SQLite database at '{_options.DatabasePath}'. The file must already exist; it is never created automatically.",
                ex);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
