namespace cinemark.checkout.api.requests;

/// <summary>
/// Per-request counter of HTTP attempts. It travels in HttpRequestMessage.Options and is
/// incremented by <see cref="AttemptCountingHandler"/>, which sits inside the retry pipeline.
/// </summary>
public sealed class AttemptCounter
{
    public static readonly HttpRequestOptionsKey<AttemptCounter> OptionsKey = new("cinemark.payment.attempts");

    private int _value;

    public int Value => Volatile.Read(ref _value);

    public void Increment() => Interlocked.Increment(ref _value);
}

/// <summary>Counts every attempt that actually leaves the resilience pipeline.</summary>
public sealed class AttemptCountingHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Options.TryGetValue(AttemptCounter.OptionsKey, out var counter))
        {
            counter.Increment();
        }

        return base.SendAsync(request, cancellationToken);
    }
}
