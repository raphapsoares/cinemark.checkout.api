using cinemark.checkout.shared.enums;

namespace cinemark.checkout.api.models;

// Cobrança no provedor de cartão: o que é enviado, o que volta e o que fica gravado.

/// <summary>Body of POST /charges sent to the card provider.</summary>
public sealed record ChargeHttpRequest(Guid OrderId, decimal Amount);

/// <summary>
/// Body returned by the card provider: 200 with status "approved", or 402 with status "declined".
/// </summary>
public sealed record ChargeHttpResponse(string Status, string? Reference)
{
    public const string ApprovedStatus = "approved";
    public const string DeclinedStatus = "declined";
}

/// <summary>Resposta final do provedor. <see cref="Attempts"/> conta as tentativas HTTP, retries incluídos.</summary>
public sealed record ChargeResult(ChargeOutcome Outcome, string? ProviderReference, int Attempts);

/// <summary>Pagamento gravado na tabela payments.</summary>
public sealed record PaymentRecord(
    Guid Id,
    Guid OrderId,
    string IdempotencyKey,
    decimal Amount,
    PaymentStatus Status,
    string? ProviderReference,
    int Attempts,
    DateTimeOffset UpdatedAt);
