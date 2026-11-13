using System.ComponentModel;
using Busynet.Cli.Infrastructure;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Busynet.Cli.Features.ClaudeLimits;

public sealed class ClaudeLimitsSettings : BarCommandSettings
{
    public const int DefaultInterval = 180;

    [CommandOption("--interval <SECONDS>")]
    [Description("Seconds between usage fetches.")]
    [DefaultValue(DefaultInterval)]
    public int Interval { get; set; } = DefaultInterval;

    public override ValidationResult Validate()
    {
        var result = base.Validate();
        if (!result.Successful)
            return result;
        return Interval < 1 ? ValidationResult.Error("--interval must be at least 1 second.") : ValidationResult.Success();
    }
}
