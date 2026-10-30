using System.Net.Http.Json;
using System.Text.Json;
using cinemark.checkout.api.requests;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace cinemark.checkout.api.tests.support;

/// <summary>
/// Sobe a API real em memória (WebApplicationFactory) sobre um banco temporário. A configuração é a do
/// appsettings.json da API; aqui só mudam o banco, os tempos de resiliência e o provedor simulado, que fica
/// determinístico (sem falhas aleatórias, salvo o que cada teste pedir).
/// </summary>
internal sealed class CheckoutTestHost : IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    private CheckoutTestHost(TestDatabase database, Action<FakePaymentHandlerOptions>? configureProvider)
    {
        Database = database;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:DatabasePath"] = database.Path,
                ["Database:ResetOnStartup"] = "false", // o banco do teste já vem pronto do TestDatabase
                ["PaymentResilience:AttemptTimeout:Timeout"] = "00:00:01",
                ["PaymentResilience:Retry:Delay"] = "00:00:00.001",
                ["FakePayment:FailureRate"] = "0",
                ["FakePayment:DeclineRate"] = "0",
                ["FakePayment:MinDelayMs"] = "0",
                ["FakePayment:MaxDelayMs"] = "0",
                ["FakePayment:Seed"] = "42",
            }));
            builder.ConfigureServices(services =>
                services.PostConfigure<FakePaymentHandlerOptions>(options => configureProvider?.Invoke(options)));
        });
        Client = _factory.CreateClient();
    }

    public TestDatabase Database { get; }

    public HttpClient Client { get; }

    public FakePaymentHandler PaymentProvider => _factory.Services.GetRequiredService<FakePaymentHandler>();

    public static CheckoutTestHost Create(Action<FakePaymentHandlerOptions>? configureProvider = null) =>
        new(TestDatabase.Create(), configureProvider);

    /// <summary>Corpo de checkout com os itens informados; o preço vem do catálogo.</summary>
    public static object Body(params (string ProductId, int Quantity)[] items) => new
    {
        customerId = "customer-1",
        items = items.Select(item => new { productId = item.ProductId, quantity = item.Quantity }),
    };

    /// <param name="simulatePayment">Valor do cabeçalho X-Simulate-Payment (decline, unavailable, timeout).</param>
    public async Task<CheckoutHttpResponse> PostCheckoutAsync(string? idempotencyKey, object body, string? simulatePayment = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/checkout") { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        if (simulatePayment is not null)
        {
            request.Headers.Add(FakePaymentHandler.SimulateHeader, simulatePayment);
        }

        using var response = await Client.SendAsync(request);
        return new CheckoutHttpResponse((int)response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>GET /orders/{id}: pedido, itens, reservas e pagamento.</summary>
    public async Task<JsonElement> GetOrderAsync(Guid orderId) =>
        JsonDocument.Parse(await Client.GetStringAsync($"/orders/{orderId}")).RootElement;

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
        Database.Dispose();
    }
}

internal sealed record CheckoutHttpResponse(int StatusCode, string Body)
{
    private JsonElement Root => JsonDocument.Parse(Body).RootElement;

    public Guid OrderId => Root.GetProperty("orderId").GetGuid();

    public string? Status => Root.GetProperty("status").GetString();

    public string? FailureReason => Root.GetProperty("failureReason").GetString();

    public decimal TotalAmount => Root.GetProperty("totalAmount").GetDecimal();
}
