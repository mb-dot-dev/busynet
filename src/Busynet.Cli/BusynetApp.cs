using System.CommandLine;
using System.Reflection;
using Busynet.Cli.Features.ClaudeLimits;
using Busynet.Cli.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Busynet.Cli;

public static class BusynetApp
{
    public const string Name = "busynet";

    /// <summary>
    /// Parses <paramref name="args"/> and runs the command over <paramref name="services"/>. Help and version go
    /// to <paramref name="stdout"/>; every error line goes to <paramref name="stderr"/>. Cancelling
    /// <paramref name="cancellationToken"/> stops a running command with exit 0.
    /// </summary>
    public static async Task<int> RunAsync(
        string[] args,
        IServiceCollection services,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        // Disposed on every path: that flushes the loggers.
        await using var provider = services.BuildServiceProvider();
        var root = BuildRoot(provider, stdout);

        // No arguments: show the help instead of System.CommandLine's "required command" error.
        var parse = root.Parse(args.Length == 0 ? ["--help"] : args);
        if (parse.Errors.Count > 0)
        {
            foreach (var error in parse.Errors)
                stderr.WriteLine(error.Message);
            return ErrorHandler.UsageError;
        }

        var configuration = new InvocationConfiguration
        {
            Output = stdout,
            Error = stderr,
            // Exceptions reach our handler, and Ctrl+C belongs to Program (its token), not to System.CommandLine.
            EnableDefaultExceptionHandler = false,
            ProcessTerminationTimeout = null,
        };
        try
        {
            return await parse.InvokeAsync(configuration, cancellationToken);
        }
        catch (Exception e)
        {
            return new ErrorHandler(stderr, cancellationToken).Handle(e, provider);
        }
    }

    /// <summary>The informational version without its <c>+commit</c> suffix.</summary>
    public static string Version { get; } = ReadVersion();

    private static RootCommand BuildRoot(IServiceProvider provider, TextWriter stdout)
    {
        var root = new RootCommand("Console application to display various information on a BUSY Bar.");

        // The built-in option prints the version with its +commit suffix; ours does not.
        foreach (var builtIn in root.Options.OfType<VersionOption>().ToList())
            root.Options.Remove(builtIn);
        var version = new Option<bool>("--version", "-v") { Description = "Prints version information" };
        root.Options.Add(version);
        root.SetAction(parse =>
        {
            if (parse.GetValue(version))
                stdout.WriteLine(Version);
            return 0;
        });

        var claude = new Command("claude", "Claude Code on the bar.");
        claude.Subcommands.Add(ClaudeLimitsCommand.Define(provider));
        root.Subcommands.Add(claude);
        return root;
    }

    private static string ReadVersion()
    {
        var version = typeof(BusynetApp).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }
}
