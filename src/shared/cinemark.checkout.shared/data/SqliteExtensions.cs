using System.Globalization;
using Microsoft.Data.Sqlite;

namespace cinemark.checkout.shared.data;

/// <summary>
/// Utilitários de ADO.NET usados pelos repositórios.
/// </summary>
public static class SqliteExtensions
{
    private const int SqliteConstraint = 19;
    private const int SqliteConstraintPrimaryKey = 1555;
    private const int SqliteConstraintUnique = 2067;

    public static SqliteCommand CreateCommand(this SqliteConnection connection, string sql, SqliteTransaction? transaction = null)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return command;
    }

    public static SqliteCommand WithParameter(this SqliteCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    public static bool IsUniqueViolation(this SqliteException exception) =>
        exception.SqliteErrorCode == SqliteConstraint
        && exception.SqliteExtendedErrorCode is SqliteConstraintUnique or SqliteConstraintPrimaryKey;

    /// <summary>
    /// Desfaz o que foi feito desde o savepoint e o descarta; o restante da transação continua válido.
    /// Não aceita cancelamento: desfazer pela metade deixaria a transação inconsistente.
    /// </summary>
    public static async Task RollbackToSavepointAsync(this SqliteTransaction transaction, string savepoint)
    {
        await transaction.RollbackAsync(savepoint, CancellationToken.None);
        await transaction.ReleaseAsync(savepoint, CancellationToken.None);
    }

    /// <summary>
    /// Converte o inteiro lido de uma coluna de status no enum. Valor fora do enum indica dado corrompido
    /// ou esquema divergente, então falha em vez de seguir com um status inválido.
    /// </summary>
    public static TEnum ToEnum<TEnum>(int value)
        where TEnum : struct, Enum =>
        Enum.IsDefined(typeof(TEnum), value)
            ? (TEnum)Enum.ToObject(typeof(TEnum), value)
            : throw new InvalidOperationException($"{value} is not a valid {typeof(TEnum).Name}.");

    /// <summary>
    /// Formata um valor em dinheiro para as colunas TEXT (ex.: 25.00): sempre 2 casas e cultura invariante,
    /// para o valor ficar exato (sem ponto flutuante) e com um formato só no banco.
    /// </summary>
    public static string ToMoneyText(this decimal value) =>
        decimal.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);

    public static decimal ParseMoney(string value) =>
        decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);

    /// <summary>Formata a data como texto ISO 8601 em UTC, por exemplo 2026-01-31T12:00:00.0000000Z.</summary>
    public static string ToIsoUtc(this DateTimeOffset value) =>
        value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    public static DateTimeOffset ParseIsoUtc(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
