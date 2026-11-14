using Busynet.Bkk.Departures;
using Busynet.Cli.Features.BkkDepartures;

namespace Busynet.Cli.Tests.Features.BkkDepartures;

public sealed class DepartureLabelsTests
{
    internal static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(1), "test", "test");
    internal static readonly DateTimeOffset Now = new(2026, 11, 14, 22, 47, 0, TimeSpan.Zero);

    internal static Departure Live(TimeSpan inFromNow, string line = "922", string headsign = "Astoria M ► Budakeszi") =>
        new(line, headsign, Now + inFromNow - TimeSpan.FromMinutes(1), Now + inFromNow);

    internal static Departure Scheduled(TimeSpan inFromNow, string line = "940", string headsign = "Kamaraerdő") =>
        new(line, headsign, Now + inFromNow, null);

    private static TimeSpan M(double minutes) => TimeSpan.FromMinutes(minutes);

    [Fact]
    public void Shows_whole_minutes_for_live_departures()
    {
        Assert.Equal("17'", DepartureLabels.Time(Live(M(17.5)), Now, Zone));
        Assert.Equal("0'", DepartureLabels.Time(Live(TimeSpan.FromSeconds(30)), Now, Zone));
        Assert.Equal("0'", DepartureLabels.Time(Live(TimeSpan.Zero), Now, Zone));
    }

    [Fact]
    public void Marks_scheduled_departures() =>
        Assert.Equal("~17'", DepartureLabels.Time(Scheduled(M(17)), Now, Zone));

    [Fact]
    public void Switches_to_clock_time_at_one_hour()
    {
        Assert.Equal("59'", DepartureLabels.Time(Live(M(59) + TimeSpan.FromSeconds(59)), Now, Zone));
        Assert.Equal("00:47", DepartureLabels.Time(Live(M(60)), Now, Zone));
        Assert.Equal("~01:04", DepartureLabels.Time(Scheduled(M(77)), Now, Zone));
    }

    [Fact]
    public void Header_numbers_from_one() =>
        Assert.Equal("1. 922 17'", DepartureLabels.Header(1, Live(M(17)), Now, Zone));
}
