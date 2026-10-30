using cinemark.checkout.api.models;

namespace cinemark.checkout.api.interfaces;

/// <summary>Consultas só de leitura usadas pelos GETs da API, para conferir o resultado de um checkout.</summary>
public interface ICheckoutQueries
{
    Task<OrderDetails?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken);

    Task<ProductRecord?> GetProductAsync(string productId, CancellationToken cancellationToken);
}
