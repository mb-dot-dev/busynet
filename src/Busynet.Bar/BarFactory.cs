using System.Reactive.Concurrency;
using Busynet.Bar.Buttons;
using Busynet.Bar.Display;
using Busynet.Bar.Http;
using Microsoft.Extensions.Logging;
using Refit;

namespace Busynet.Bar;

internal sealed class BarFactory(IHttpClientFactory httpClients, ILoggerFactory loggerFactory) : IBarFactory
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

    public IObservable<ButtonEvent> CreateButtonEvents(BarConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        return ButtonEventStream.Create(
            ct => StatusSocket.ReadFramesAsync(connection, ct),
            DefaultScheduler.Instance,
            loggerFactory.CreateLogger(typeof(ButtonEventStream)));
    }
}
