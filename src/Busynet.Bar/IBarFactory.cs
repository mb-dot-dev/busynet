namespace Busynet.Bar;

public interface IBarFactory
{
    IBarDisplay CreateDisplay(BarConnection connection);
}
