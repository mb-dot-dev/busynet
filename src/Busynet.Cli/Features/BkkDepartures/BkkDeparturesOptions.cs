using System.CommandLine;
using System.CommandLine.Parsing;
using Busynet.Cli.Infrastructure;

namespace Busynet.Cli.Features.BkkDepartures;

/// <summary>
/// The arguments of <c>busynet bkk departures</c>: the optional <c>STOP_ID</c>, <c>--interval</c>, <c>--rotate</c> and
/// the shared bar options. The API key is deliberately not an option: it comes from <c>BKK_API_KEY</c> only.
/// </summary>
public sealed class BkkDeparturesOptions
{
    private readonly BarOptions _bar;

    public BkkDeparturesOptions(BarOptions bar)
    {
        ArgumentNullException.ThrowIfNull(bar);

        _bar = bar;
        StopId = new Argument<string>("STOP_ID")
        {
            Description = "BKK stop id; the default is Kisfaludy utca, Budaörs.",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => BkkDeparturesSettings.DefaultStopId,
        };
        StopId.Validators.Add(result =>
        {
            if (result.Tokens.Count > 0 && string.IsNullOrWhiteSpace(result.Tokens[0].Value))
                result.AddError("STOP_ID must not be empty.");
        });

        Interval = Seconds("--interval", "Seconds between polls.", BkkDeparturesSettings.DefaultInterval);
        Rotate = Seconds("--rotate", "Seconds each departure is shown.", BkkDeparturesSettings.DefaultRotate);
    }

    public Argument<string> StopId { get; }

    public Option<int> Interval { get; }

    public Option<int> Rotate { get; }

    public void AddTo(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        command.Arguments.Add(StopId);
        command.Options.Add(Interval);
        command.Options.Add(Rotate);
        _bar.AddTo(command);
    }

    public BkkDeparturesSettings Read(ParseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var bar = _bar.Read(result);
        return new BkkDeparturesSettings(
            bar.Host,
            bar.Token,
            bar.Verbose,
            result.GetValue(StopId)!.Trim(),
            result.GetValue(Interval),
            result.GetValue(Rotate));
    }

    private static Option<int> Seconds(string name, string description, int defaultValue)
    {
        var option = new Option<int>(name)
        {
            Description = description,
            DefaultValueFactory = _ => defaultValue,
        };
        option.Validators.Add(result =>
        {
            // Read the raw token: the typed value throws when the conversion itself failed, and that
            // failure is already reported as a parse error.
            if (result.Tokens.Count > 0 && int.TryParse(result.Tokens[0].Value, out var seconds) && seconds < 1)
                result.AddError($"{name} must be at least 1 second.");
        });
        return option;
    }
}
