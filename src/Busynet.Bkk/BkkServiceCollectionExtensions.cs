using Busynet.Bkk.Departures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Refit;

namespace Busynet.Bkk;

public static class BkkServiceCollectionExtensions
{
    internal const string HttpClientName = "bkk";

    public static IServiceCollection AddBkk(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The API key is in the request URL: never let IHttpClientFactory log it.
        services.AddHttpClient(HttpClientName, c =>
        {
            c.BaseAddress = new Uri("https://futar.bkk.hu");
            c.Timeout = FutarOptions.DefaultTimeout;
        }).RemoveAllLoggers();

        services.TryAddSingleton(FutarOptions.Default);
        services.TryAddTransient<IFutarApi>(sp =>
            RestService.ForGenerated<IFutarApi>(sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName)));
        services.TryAddTransient<IBkkDepartures, BkkDeparturesClient>();
        return services;
    }
}
