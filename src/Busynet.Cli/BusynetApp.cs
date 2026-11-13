using System.Reflection;
using Busynet.Cli.Features.ClaudeLimits;
using Busynet.Cli.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Busynet.Cli;

public static class BusynetApp
{
    public const string Name = "busynet";

    /// <summary>
    /// Parses <paramref name="args"/> and runs the command over <paramref name="services"/>. Our error lines
    /// go to <paramref name="stderr"/>; help and version go to <paramref name="console"/> (Spectre's default
    /// console when null). Cancelling <paramref name="cancellationToken"/> stops a running command with exit 0.
    /// </summary>
    public static Task<int> RunAsync(
        string[] args,
        IServiceCollection services,
        TextWriter stderr,
        CancellationToken cancellationToken,
        IAnsiConsole? console = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(stderr);

        var errors = new ErrorHandler(stderr, cancellationToken);
        var app = new CommandApp(new TypeRegistrar(services));
        app.Configure(config =>
        {
            config.SetApplicationName(Name);
            config.SetApplicationVersion(Version);
            config.UseStrictParsing();
            if (console is not null)
                config.ConfigureConsole(console);
            config.SetExceptionHandler(errors.Handle);

            config.AddBranch("claude", claude =>
            {
                claude.SetDescription("Claude Code on the bar.");
                claude.AddCommand<ClaudeLimitsCommand>("limits").WithDescription(ClaudeLimitsCommand.Description);
            });
        });

        return app.RunAsync(args, cancellationToken);
    }

    /// <summary>The informational version without its <c>+commit</c> suffix.</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var version = typeof(BusynetApp).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }
}
