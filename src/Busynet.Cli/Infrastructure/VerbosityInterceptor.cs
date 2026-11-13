using Spectre.Console.Cli;

namespace Busynet.Cli.Infrastructure;

/// <summary>Copies <c>--verbose</c> into the run's <see cref="Verbosity"/>, which the logging filter reads.</summary>
public sealed class VerbosityInterceptor(Verbosity verbosity) : ICommandInterceptor
{
    public void Intercept(CommandContext context, CommandSettings settings)
    {
        if (settings is BarCommandSettings { Verbose: true })
            verbosity.IsVerbose = true;
    }
}
