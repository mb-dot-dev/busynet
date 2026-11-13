using System.CommandLine;
using Busynet.Cli.Infrastructure;

namespace Busynet.Cli.Features.ClaudeLimits;

/// <summary>The options of <c>busynet claude limits</c>: the shared bar options plus <c>--interval</c>.</summary>
public sealed class ClaudeLimitsOptions
{
    private readonly BarOptions _bar;

    public ClaudeLimitsOptions(BarOptions bar)
    {
        ArgumentNullException.ThrowIfNull(bar);

        _bar = bar;
        Interval = new Option<int>("--interval")
        {
            Description = "Seconds between usage fetches.",
            DefaultValueFactory = _ => ClaudeLimitsSettings.DefaultInterval,
        };
        Interval.Validators.Add(result =>
        {
            // Read the raw token: the typed value throws when the conversion itself failed, and that
            // failure is already reported as a parse error.
            if (result.Tokens.Count > 0 && int.TryParse(result.Tokens[0].Value, out var seconds) && seconds < 1)
                result.AddError("--interval must be at least 1 second.");
        });
    }

    public Option<int> Interval { get; }

    public void AddTo(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        _bar.AddTo(command);
        command.Options.Add(Interval);
    }

    public (BarSettings Bar, int Interval) Read(ParseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return (_bar.Read(result), result.GetValue(Interval));
    }
}
