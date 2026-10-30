using System.Net;
using cinemark.checkout.api.interfaces;
using cinemark.checkout.api.models;
using cinemark.checkout.shared.enums;
using cinemark.checkout.shared.exceptions;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace cinemark.checkout.api.requests;

public sealed class CardPaymentProvider : IPaymentProvider
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public static readonly Uri DefaultBaseAddress = new("https://card-provider.local/");

    private readonly HttpClient _httpClient;

    public CardPaymentProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ChargeResult> ChargeAsync(Guid orderId, decimal amount, string idempotencyKey, CancellationToken cancellationToken)
    {
        var attempts = new AttemptCounter();
        using var message = new HttpRequestMessage(HttpMethod.Post, "charges")
        {
            Content = JsonContent.Create(new ChargeHttpRequest(orderId, amount)),
        };
        message.Headers.Add(IdempotencyKeyHeader, idempotencyKey);
        message.Options.Set(AttemptCounter.OptionsKey, attempts);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(message, cancellationToken);
        }
        catch (BrokenCircuitException ex)
        {
            throw new PaymentUnavailableException("Payment provider circuit is open.", attempts.Value, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutRejectedException
                                   || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new PaymentUnavailableException("Payment provider is unavailable.", attempts.Value, ex);
        }

        using (response)
        {
            if (IsUnavailable(response.StatusCode))
            {
                throw new PaymentUnavailableException(
                    $"Payment provider is unavailable (HTTP {(int)response.StatusCode}).", attempts.Value);
            }

            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.PaymentRequired)
            {
                throw new InvalidOperationException(
                    $"Unexpected response from the payment provider (HTTP {(int)response.StatusCode}).");
            }

            var body = await response.Content.ReadFromJsonAsync<ChargeHttpResponse>(cancellationToken)
                       ?? throw new InvalidOperationException("Empty response from the payment provider.");

            var outcome = string.Equals(body.Status, ChargeHttpResponse.ApprovedStatus, StringComparison.OrdinalIgnoreCase)
                ? ChargeOutcome.Approved
                : ChargeOutcome.Declined;

            return new ChargeResult(outcome, body.Reference, attempts.Value);
        }
    }

    // Mesmos status que o handler de resiliência repete; chegando aqui, os retries já se esgotaram.
    private static bool IsUnavailable(HttpStatusCode statusCode) =>
        (int)statusCode >= 500
        || statusCode == HttpStatusCode.RequestTimeout
        || statusCode == HttpStatusCode.TooManyRequests;
}
