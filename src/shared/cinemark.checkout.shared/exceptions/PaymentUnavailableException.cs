namespace cinemark.checkout.shared.exceptions;

/// <summary>
/// O provedor de pagamento não deu resposta final: as falhas transitórias esgotaram os retries
/// ou o circuit breaker está aberto.
/// </summary>
public sealed class PaymentUnavailableException : Exception
{
    public PaymentUnavailableException(string message, int attempts, Exception? innerException = null)
        : base(message, innerException)
    {
        Attempts = attempts;
    }

    /// <summary>Tentativas HTTP que chegaram ao provedor (0 quando o circuito estava aberto).</summary>
    public int Attempts { get; }
}
