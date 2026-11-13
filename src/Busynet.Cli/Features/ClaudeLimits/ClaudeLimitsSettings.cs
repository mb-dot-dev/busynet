using Busynet.Cli.Infrastructure;

namespace Busynet.Cli.Features.ClaudeLimits;

public sealed record ClaudeLimitsSettings(string? Host, string? Token, bool Verbose, int Interval)
    : BarSettings(Host, Token, Verbose)
{
    public const int DefaultInterval = 180;
}
