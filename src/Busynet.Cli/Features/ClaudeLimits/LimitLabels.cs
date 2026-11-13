using System.Globalization;
using Busynet.Claude.Usage;

namespace Busynet.Cli.Features.ClaudeLimits;

/// <summary>The short bar labels, ported from busyboy's <c>watch.py</c>.</summary>
public static class LimitLabels
{
    /// <summary>The time to the 5-hour reset: <c>3h59m</c>, <c>3h05m</c> or <c>59m</c>; <c>0m</c> once past.</summary>
    public static string SessionCountdown(TimeSpan remaining)
    {
        var (hours, minutes) = Clock(remaining);
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}h{minutes:D2}m")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}m");
    }

    /// <summary>
    /// The time to the weekly reset: whole days (<c>6d</c>) from 48h, whole hours (<c>47h</c>) under that,
    /// the session form under an hour. Finer forms make <c>wk 100% ...</c> wider than the display.
    /// </summary>
    public static string WeeklyLabelTime(TimeSpan remaining)
    {
        if (remaining >= TimeSpan.FromHours(48))
            return string.Create(CultureInfo.InvariantCulture, $"{remaining.Days}d");

        var (hours, _) = Clock(remaining);
        return hours > 0 ? string.Create(CultureInfo.InvariantCulture, $"{hours}h") : SessionCountdown(remaining);
    }

    /// <summary>
    /// <c>5h 85% 3h49m</c> or <c>wk 40% 6d</c>; the time is left off without a reset time. The percent is
    /// clamped to 0-100 and rounded half to even so the label never outgrows the display.
    /// </summary>
    public static string Format(LimitKind kind, UsageWindow window, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(window);

        var (name, time) = kind == LimitKind.Session
            ? ("5h", (Func<TimeSpan, string>)SessionCountdown)
            : ("wk", WeeklyLabelTime);
        var percent = (int)Math.Round(Math.Clamp(window.Utilization, 0, 100), MidpointRounding.ToEven);
        var label = string.Create(CultureInfo.InvariantCulture, $"{name} {percent}%");
        return window.ResetsAt is { } resetsAt ? $"{label} {time(resetsAt - now)}" : label;
    }

    // Whole hours and minutes left, floored, never negative.
    private static (long Hours, long Minutes) Clock(TimeSpan remaining)
    {
        var minutes = Math.Max((long)Math.Floor(remaining.TotalMinutes), 0);
        return (minutes / 60, minutes % 60);
    }
}
