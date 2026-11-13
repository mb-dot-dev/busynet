using Busynet.Bar.Buttons;

namespace Busynet.Bar;

public interface IBarFactory
{
    IBarDisplay CreateDisplay(BarConnection connection);

    /// <summary>
    /// The bar's physical button events: lazy, shared, reconnecting, and never errors or completes.
    /// </summary>
    IObservable<ButtonEvent> CreateButtonEvents(BarConnection connection);
}
