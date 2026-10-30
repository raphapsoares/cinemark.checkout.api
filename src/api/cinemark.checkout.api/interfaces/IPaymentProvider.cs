using cinemark.checkout.api.models;

namespace cinemark.checkout.api.interfaces;

public interface IPaymentProvider
{
    Task<ChargeResult> ChargeAsync(Guid orderId, decimal amount, string idempotencyKey, CancellationToken cancellationToken);
}
