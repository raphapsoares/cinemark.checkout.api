using System.Diagnostics.CodeAnalysis;
using cinemark.checkout.api.models;

namespace cinemark.checkout.api.validators;

public static class CheckoutRequestValidator
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const int MaxIdempotencyKeyLength = 128;

    public static bool TryValidate(
        [NotNullWhen(true)] string? idempotencyKey,
        [NotNullWhen(true)] CheckoutRequest? body,
        out string error)
    {
        if (string.IsNullOrEmpty(idempotencyKey) || idempotencyKey.Length > MaxIdempotencyKeyLength)
        {
            error = $"The {IdempotencyKeyHeader} header is required (max {MaxIdempotencyKeyLength} characters).";
            return false;
        }

        if (body is null)
        {
            error = "Request body is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(body.CustomerId))
        {
            error = "customerId is required.";
            return false;
        }

        if (body.Items is not { Count: > 0 })
        {
            error = "items must contain at least one item.";
            return false;
        }

        var productIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in body.Items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.ProductId))
            {
                error = "Every item needs a productId.";
                return false;
            }

            if (!productIds.Add(item.ProductId))
            {
                error = $"Product '{item.ProductId}' appears more than once.";
                return false;
            }

            if (item.Quantity <= 0)
            {
                error = $"Quantity for product '{item.ProductId}' must be greater than zero.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }
}
