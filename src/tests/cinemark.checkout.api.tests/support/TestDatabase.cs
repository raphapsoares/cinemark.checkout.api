using cinemark.checkout.shared.data;
using cinemark.checkout.shared.enums;
using Microsoft.Data.Sqlite;

namespace cinemark.checkout.api.tests.support;

/// <summary>
/// Arquivo SQLite temporário por teste. Aplica database/schema.sql, o mesmo esquema do banco local
/// (o código de produção nunca cria esquema), e apaga o arquivo no Dispose.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    // Mesmo esquema usado para criar o banco local (database/schema.sql), copiado para a saída do projeto de teste.
    private static readonly string Schema =
        File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "database", "schema.sql"));

    private TestDatabase(string path)
    {
        Path = path;
        Options = new DatabaseOptions { DatabasePath = path };
    }

    public string Path { get; }

    public DatabaseOptions Options { get; }

    public static TestDatabase Create()
    {
        var database = new TestDatabase(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"cinemark-test-{Guid.NewGuid():N}.db"));
        database.Execute(Schema, create: true);
        return database;
    }

    public void InsertProduct(string id, int availableQuantity, decimal unitPrice = 25.00m) =>
        Execute(
            "INSERT INTO products (id, name, available_quantity, unit_price) VALUES (@id, @name, @quantity, @price)",
            ("@id", id), ("@name", $"Product {id}"), ("@quantity", availableQuantity), ("@price", unitPrice.ToMoneyText()));

    public long GetAvailableQuantity(string productId) =>
        Scalar<long>("SELECT available_quantity FROM products WHERE id = @id", ("@id", productId));

    public ReservationStatus GetReservationStatus(Guid orderId) =>
        (ReservationStatus)Scalar<long>("SELECT status FROM reservations WHERE order_id = @id", ("@id", orderId.ToString()));

    public PaymentStatus GetPaymentStatus(Guid orderId) =>
        (PaymentStatus)Scalar<long>("SELECT status FROM payments WHERE order_id = @id", ("@id", orderId.ToString()));

    public long Count(string table) => Scalar<long>($"SELECT COUNT(*) FROM {table}");

    public T Scalar<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        using var connection = Open(create: false);
        using var command = Command(connection, sql, parameters);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? default! : (T)Convert.ChangeType(value, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        SqliteConnection.ClearPool(new SqliteConnection(SqliteConnectionFactory.BuildConnectionString(Options)));
        try
        {
            File.Delete(Path);
        }
        catch (IOException)
        {
            // Uma conexão ainda pode estar fechando; a pasta temporária do sistema é limpa depois.
        }
    }

    private void Execute(string sql, params (string Name, object? Value)[] parameters) => Execute(sql, create: false, parameters);

    private void Execute(string sql, bool create, params (string Name, object? Value)[] parameters)
    {
        using var connection = Open(create);
        using var command = Command(connection, sql, parameters);
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open(bool create)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = create ? SqliteOpenMode.ReadWriteCreate : SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql, (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }
}
