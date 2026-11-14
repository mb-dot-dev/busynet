using Busynet.Bar.Layouts;
using Busynet.Bkk.Departures;
using Busynet.Cli.Features.BkkDepartures;
using static Busynet.Cli.Tests.Features.BkkDepartures.DepartureLabelsTests;

namespace Busynet.Cli.Tests.Features.BkkDepartures;

public sealed class DeparturesStateTests
{
    private static TimeSpan M(double minutes) => TimeSpan.FromMinutes(minutes);

    private static StopDepartures Stop(params Departure[] departures) => new("Kötő utca", departures);

    private static readonly StopDepartures Five = Stop(
        Live(M(2), "922"), Live(M(4), "40"), Scheduled(M(9), "940"), Live(M(12), "88"), Live(M(15), "40E"));

    private static DepartureFrame? Render(DeparturesState state) => state.Render(Now, Zone);

    [Fact]
    public void Renders_nothing_before_the_first_poll() =>
        Assert.Null(Render(new DeparturesState(null, 0)));

    [Fact]
    public void Rotates_through_the_first_three()
    {
        var headers = Enumerable.Range(0, 4).Select(i => Render(new DeparturesState(Five, i))!.Header);

        Assert.Equal(["1. 922 2'", "2. 40 4'", "3. 940 ~9'", "1. 922 2'"], headers);
    }

    [Fact]
    public void Destination_is_transliterated() =>
        Assert.Equal("Astoria M > Budakeszi", Render(new DeparturesState(Five, 0))!.Destination);

    [Fact]
    public void Rotates_through_fewer_than_three()
    {
        var two = Stop(Live(M(2), "922"), Live(M(4), "40"));

        Assert.StartsWith("1. ", Render(new DeparturesState(two, 2))!.Header, StringComparison.Ordinal);
    }

    [Fact]
    public void No_departures_shows_the_stop_name() =>
        Assert.Equal(new DepartureFrame("no departures", "Koto utca"), Render(new DeparturesState(Stop(), 0)));

    [Fact]
    public void Departed_entries_drop_out_before_the_next_poll()
    {
        var stop = Stop(Live(TimeSpan.FromSeconds(-1), "922"), Live(M(5), "40"), Live(M(9), "88"));

        Assert.Equal("1. 40 5'", Render(new DeparturesState(stop, 0))!.Header);
        Assert.Equal(new DepartureFrame("no departures", "Koto utca"),
            Render(new DeparturesState(Stop(Live(TimeSpan.FromSeconds(-1))), 0)));
    }

    [Fact]
    public void Index_wraps_when_the_list_shrinks()
    {
        var state = new DeparturesState(Five, 2).WithSnapshot(Stop(Live(M(3), "88")));

        Assert.Equal("1. 88 3'", Render(state)!.Header);
    }

    [Fact]
    public void Advance_moves_to_the_next_departure() =>
        Assert.Equal("2. 40 4'", Render(new DeparturesState(Five, 0).Advance())!.Header);
}
