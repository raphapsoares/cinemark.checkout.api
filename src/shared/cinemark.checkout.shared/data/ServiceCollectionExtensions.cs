using cinemark.checkout.shared.interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace cinemark.checkout.shared.data;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSqliteDatabase(this IServiceCollection services, Action<DatabaseOptions> configure)
    {
        services.AddOptions<DatabaseOptions>().Configure(configure);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IDbConnectionFactory, SqliteConnectionFactory>();
        services.TryAddSingleton<ITransactionRunner, SqliteTransactionRunner>();
        return services;
    }
}
