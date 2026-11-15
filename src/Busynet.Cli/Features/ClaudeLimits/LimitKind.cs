namespace Busynet.Cli.Features.ClaudeLimits;

/// <summary>Which Claude usage limit the bar shows; the Start button toggles between them.</summary>
public enum LimitKind
{
    Session,
    Weekly,
}

/// <summary>The length of each limit's time window.</summary>
public static class LimitWindows
{
    public static TimeSpan Of(LimitKind kind) =>
        kind == LimitKind.Session ? TimeSpan.FromHours(5) : TimeSpan.FromDays(7);
}
