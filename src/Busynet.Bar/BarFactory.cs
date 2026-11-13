using Refit;

namespace Busynet.Bar;

internal sealed class BarFactory(IHttpClientFactory httpClients) : IBarFactory
{
    public IBarDisplay CreateDisplay(BarConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var client = httpClients.CreateClient(BarServiceCollectionExtensions.HttpClientName);
        client.BaseAddress = connection.HttpBaseAddress;
        if (connection.Token is not null)
            client.DefaultRequestHeaders.Add("X-API-Token", connection.Token);

        var settings = new RefitSettings(new SystemTextJsonContentSerializer(BarJson.Options));
        return new BarDisplay(RestService.For<IBusyBarApi>(client, settings));
    }
}
