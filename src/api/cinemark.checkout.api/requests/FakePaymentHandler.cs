using System.Net;
using cinemark.checkout.api.models;

namespace cinemark.checkout.api.requests;

/// <summary>Comportamento do provedor simulado (seção "FakePayment" do appsettings.json).</summary>
public sealed class FakePaymentHandlerOptions
{
    public const string SectionName = "FakePayment";

    public double FailureRate { get; set; } = 0.1;

    public double DeclineRate { get; set; } = 0.1;

    public int MinDelayMs { get; set; } = 50;

    public int MaxDelayMs { get; set; } = 300;

    public int? Seed { get; set; }

    public int FailFirstN { get; set; }
}

/// <summary>
/// Provedor de cartão simulado, usado como handler principal do HttpClient. Sem o cabeçalho
/// <see cref="SimulateHeader"/>, segue as taxas da configuração; com ele, força o resultado da cobrança.
/// </summary>
public sealed class FakePaymentHandler : HttpMessageHandler
{
    /// <summary>Cabeçalho do POST /checkout que força um resultado no provedor (só para demonstração).</summary>
    public const string SimulateHeader = "X-Simulate-Payment";

    /// <summary>Cartão recusado (HTTP 402): resposta final, sem retry.</summary>
    public const string SimulateDecline = "decline";

    /// <summary>Provedor fora do ar (HTTP 503) em toda tentativa.</summary>
    public const string SimulateUnavailable = "unavailable";

    /// <summary>Provedor lento: não responde até a tentativa estourar o timeout.</summary>
    public const string SimulateTimeout = "timeout";

    private readonly FakePaymentHandlerOptions _options;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly Random _random;
    private readonly object _sync = new();
    private readonly Dictionary<string, ChargeHttpResponse> _processed = new(StringComparer.Ordinal);
    private int _callCount;

    public FakePaymentHandler(FakePaymentHandlerOptions options, IHttpContextAccessor? httpContextAccessor = null)
    {
        if (options.FailureRate is < 0 or > 1 || options.DeclineRate is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "FailureRate and DeclineRate must be between 0 and 1.");
        }

        if (options.MinDelayMs < 0 || options.MaxDelayMs < options.MinDelayMs)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Delays must satisfy 0 <= MinDelayMs <= MaxDelayMs.");
        }

        _options = options;
        _httpContextAccessor = httpContextAccessor;
        _random = options.Seed is { } seed ? new Random(seed) : new Random();
    }

    /// <summary>Number of requests that reached the provider.</summary>
    public int CallCount => Volatile.Read(ref _callCount);

    /// <summary>Number of distinct charges processed (approved or declined).</summary>
    public int ProcessedChargeCount
    {
        get
        {
            lock (_sync)
            {
                return _processed.Count;
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref _callCount);
        var simulate = _httpContextAccessor?.HttpContext?.Request.Headers[SimulateHeader].ToString().Trim().ToLowerInvariant();

        if (simulate == SimulateTimeout)
        {
            // Só termina quando o timeout da tentativa cancela o token.
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        int delayMs;
        lock (_sync)
        {
            delayMs = _random.Next(_options.MinDelayMs, _options.MaxDelayMs + 1);
        }

        if (delayMs > 0)
        {
            await Task.Delay(delayMs, cancellationToken);
        }

        if (simulate == SimulateUnavailable || call <= _options.FailFirstN)
        {
            return Unavailable();
        }

        if (!request.Headers.TryGetValues("Idempotency-Key", out var values)
            || values.FirstOrDefault() is not { Length: > 0 } key)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }

        ChargeHttpResponse result;
        lock (_sync)
        {
            if (!_processed.TryGetValue(key, out result!))
            {
                if (simulate != SimulateDecline && _random.NextDouble() < _options.FailureRate)
                {
                    return Unavailable();
                }

                result = simulate == SimulateDecline || _random.NextDouble() < _options.DeclineRate
                    ? new ChargeHttpResponse(ChargeHttpResponse.DeclinedStatus, null)
                    : new ChargeHttpResponse(ChargeHttpResponse.ApprovedStatus, $"ch_{_random.NextInt64():x16}");

                _processed[key] = result;
            }
        }

        var status = result.Status == ChargeHttpResponse.ApprovedStatus ? HttpStatusCode.OK : HttpStatusCode.PaymentRequired;
        return new HttpResponseMessage(status) { Content = JsonContent.Create(result) };
    }

    private static HttpResponseMessage Unavailable() => new(HttpStatusCode.ServiceUnavailable);
}
