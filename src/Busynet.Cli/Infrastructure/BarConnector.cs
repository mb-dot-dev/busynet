using Busynet.Bar;
using Busynet.Bar.Http;

namespace Busynet.Cli.Infrastructure;

/// <summary>Turns the shared bar options and the environment into a connection and a display, touching nothing on the bar.</summary>
public static class BarConnector
{
    public static (BarConnection Connection, IBarDisplay Display) Connect(
        IBarFactory bars, BarSettings settings, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentNullException.ThrowIfNull(settings);

        var connection = ResolveConnection(settings, environment);
        return (connection, CreateDisplay(bars, connection));
    }

    // Values given as options were validated with the settings, so a failure here comes from the environment.
    private static BarConnection ResolveConnection(BarSettings settings, Func<string, string?> environment)
    {
        try
        {
            return BarConnection.Resolve(settings.Host, settings.Token, environment);
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            throw new ConfigurationException($"{BarConnection.HostVariable} is not a valid host.");
        }
    }

    // Creating the display is where a malformed token is rejected.
    private static IBarDisplay CreateDisplay(IBarFactory bars, BarConnection connection)
    {
        try
        {
            return bars.CreateDisplay(connection);
        }
        catch (FormatException)
        {
            throw new ConfigurationException($"{BarConnection.TokenVariable} is not a valid token.");
        }
    }
}
