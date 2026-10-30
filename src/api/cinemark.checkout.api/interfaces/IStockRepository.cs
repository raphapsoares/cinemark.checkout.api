using cinemark.checkout.api.models;
using Microsoft.Data.Sqlite;

namespace cinemark.checkout.api.interfaces;

/// <summary>Acesso a produtos e reservas, sempre dentro da transação do checkout.</summary>
public interface IStockRepository
{
    /// <summary>Preço atual de catálogo dos produtos informados. Produtos inexistentes ficam fora do dicionário.</summary>
    Task<IReadOnlyDictionary<string, decimal>> GetUnitPricesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyCollection<string> productIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Debita o estoque de todos os itens de forma atômica e grava as reservas como RESERVED.
    /// Se faltar estoque, um savepoint desfaz só os débitos, a transação do chamador continua válida e devolve false.
    /// </summary>
    Task<bool> TryReserveAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid orderId,
        IReadOnlyCollection<CheckoutRequestItem> items,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken);

    /// <summary>Devolve o estoque das reservas RESERVED e as marca RELEASED. Idempotente.</summary>
    Task ReleaseAsync(SqliteConnection connection, SqliteTransaction transaction, Guid orderId, CancellationToken cancellationToken);

    /// <summary>Marca as reservas RESERVED como CONFIRMED.</summary>
    Task ConfirmAsync(SqliteConnection connection, SqliteTransaction transaction, Guid orderId, CancellationToken cancellationToken);

    /// <summary>Muda saldo e/ou preço de catálogo (null mantém o atual). Usado só pelo endpoint de demonstração.</summary>
    /// <returns>False se o produto não existe.</returns>
    Task<bool> UpdateProductAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string productId,
        int? availableQuantity,
        decimal? unitPrice,
        CancellationToken cancellationToken);
}
