using cinemark.checkout.api.interfaces;
using cinemark.checkout.api.repositories;
using cinemark.checkout.api.requests;
using cinemark.checkout.api.services;
using cinemark.checkout.shared.data;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace cinemark.checkout.api;

public static class ServiceCollectionExtensions
{
    public const string ResilienceSectionName = "PaymentResilience";

    /// <summary>
    /// Registra o checkout a partir da configuração (appsettings.json + variáveis de ambiente): banco, repositórios,
    /// o serviço de checkout e o HttpClient tipado do provedor de cartão (handler de resiliência padrão: timeout
    /// total, retry, circuit breaker e timeout por tentativa), com o provedor simulado como handler principal.
    /// </summary>
    public static IServiceCollection AddCheckout(this IServiceCollection services, IConfiguration configuration, string contentRootPath)
    {
        services.AddSqliteDatabase(database =>
        {
            configuration.GetSection(DatabaseOptions.SectionName).Bind(database);

            // Caminho relativo (ex.: o banco local em database/) é resolvido a partir da pasta do projeto.
            if (!string.IsNullOrWhiteSpace(database.DatabasePath))
            {
                database.DatabasePath = Path.GetFullPath(database.DatabasePath, contentRootPath);
            }
        });
        services.AddOptions<FakePaymentHandlerOptions>().Bind(configuration.GetSection(FakePaymentHandlerOptions.SectionName));

        services.TryAddSingleton<IOrderRepository, SqliteOrderRepository>();
        services.TryAddSingleton<IStockRepository, SqliteStockRepository>();
        services.TryAddSingleton<IPaymentRepository, SqlitePaymentRepository>();
        services.TryAddSingleton<ICheckoutQueries, SqliteCheckoutQueries>();

        // Transiente porque depende do HttpClient tipado.
        services.TryAddTransient<ICheckoutService, CheckoutService>();

        AddCardProviderHttpClient(services, configuration.GetSection(ResilienceSectionName));
        return services;
    }

    private static void AddCardProviderHttpClient(IServiceCollection services, IConfigurationSection resilienceSection)
    {
        // O provedor simulado lê o cabeçalho X-Simulate-Payment da requisição que está sendo atendida.
        services.AddHttpContextAccessor();

        // Singleton para que o estado do fake (chaves processadas, contador) seja o mesmo para todo HttpClient.
        services.TryAddSingleton(sp => new FakePaymentHandler(
            sp.GetRequiredService<IOptions<FakePaymentHandlerOptions>>().Value,
            sp.GetRequiredService<IHttpContextAccessor>()));
        services.AddTransient<AttemptCountingHandler>();

        var httpClient = services
            .AddHttpClient<IPaymentProvider, CardPaymentProvider>(client => client.BaseAddress = CardPaymentProvider.DefaultBaseAddress)
            .ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<FakePaymentHandler>())
            // A cadeia de handlers nunca é reciclada, para não perder o estado do circuit breaker.
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

        // Retry e circuit breaker já tratam como transitórios 5xx, 408, 429, erro de rede e timeout da tentativa.
        httpClient.AddStandardResilienceHandler(resilienceSection);

        // Adicionado depois do handler de resiliência: fica dentro dele e roda uma vez por tentativa.
        httpClient.AddHttpMessageHandler<AttemptCountingHandler>();
    }
}
