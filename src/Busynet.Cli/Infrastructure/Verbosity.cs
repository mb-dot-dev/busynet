namespace Busynet.Cli.Infrastructure;

/// <summary>Whether <c>--verbose</c> was given; one per run, set before the command executes.</summary>
public sealed class Verbosity
{
    public bool IsVerbose { get; set; }
}
