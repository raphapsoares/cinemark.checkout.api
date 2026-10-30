using cinemark.checkout.api.models;

namespace cinemark.checkout.api.interfaces;

public interface ICheckoutService
{
    Task<CheckoutResult> CheckoutAsync(string idempotencyKey, CheckoutRequest request, CancellationToken cancellationToken);
}
