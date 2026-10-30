using Microsoft.Data.Sqlite;

namespace cinemark.checkout.shared.data;

/// <summary>
/// Recria o banco local a partir dos scripts de database/ (schema.sql e seed.sql). Uso só em desenvolvimento,
/// para cada start da API começar com os produtos do seed e nenhum pedido.
/// </summary>
public static class SqliteDatabaseReset
{
    public static readonly string[] Scripts = ["schema.sql", "seed.sql"];

    public static void Recreate(string databasePath)
    {
        var folder = Path.GetDirectoryName(databasePath)
                     ?? throw new InvalidOperationException($"Invalid database path '{databasePath}'.");
        var scripts = Scripts.Select(script => Path.Combine(folder, script)).ToList();
        if (scripts.FirstOrDefault(script => !File.Exists(script)) is { } missing)
        {
            throw new InvalidOperationException($"Script '{missing}' not found next to the database file.");
        }

        try
        {
            File.Delete(databasePath);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"Could not delete '{databasePath}'. Close any program using the file and start the API again.", ex);
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        connection.Open();

        foreach (var script in scripts)
        {
            using var command = connection.CreateCommand();
            command.CommandText = File.ReadAllText(script);
            command.ExecuteNonQuery();
        }
    }
}
