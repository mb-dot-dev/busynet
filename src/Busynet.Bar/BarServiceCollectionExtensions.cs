using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;

namespace Busynet.Bar;

public static class BarServiceCollectionExtensions
{
    internal const string HttpClientName = "busybar";

    public static IServiceCollection AddBusyBar(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // URLs and headers carry the token: never let IHttpClientFactory log them.
        services.AddHttpClient(HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(10)).RemoveAllLoggers();
        services.TryAddSingleton<IBarFactory, BarFactory>();
        return services;
    }
}
