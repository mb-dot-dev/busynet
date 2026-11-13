using System.CommandLine;
using System.Reactive.Concurrency;
using Busynet.Bar;
using Busynet.Bar.Http;
using Busynet.Claude.Usage;
using Busynet.Cli.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Busynet.Cli.Features.ClaudeLimits;

/// <summary>
/// <c>busynet claude limits</c>: one usage fetch first (any failure ends the command before the bar is
/// touched), then the watch, which clears, draws and keeps refreshing until Ctrl+C.
/// </summary>
public sealed class ClaudeLimitsCommand(
    IBarFactory bars,
    IClaudeUsage usage,
    IScheduler scheduler,
    Func<string, string?> environment,
    ILogger<ClaudeLimitsCommand> logger)
{
    public const string Description =
        "Watch Claude usage limits on the bar until Ctrl+C; the Start button switches between 5-hour and weekly.";

    /// <summary>The <c>limits</c> command: parses the options, then runs <see cref="ExecuteAsync"/> from <paramref name="provider"/>.</summary>
    public static Command Define(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var options = new ClaudeLimitsOptions(new BarOptions());
        var command = new Command("limits", Description);
        options.AddTo(command);
        command.SetAction((parse, ct) =>
        {
            var (bar, interval) = options.Read(parse);
            var settings = new ClaudeLimitsSettings(bar.Host, bar.Token, bar.Verbose, interval);
            // Before the command resolves: the logging filter and the error handler read it.
            provider.GetRequiredService<Verbosity>().IsVerbose = settings.Verbose;
            return provider.GetRequiredService<ClaudeLimitsCommand>().ExecuteAsync(settings, ct);
        });
        return command;
    }

    public async Task<int> ExecuteAsync(ClaudeLimitsSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var connection = ResolveConnection(settings);
            var display = CreateDisplay(connection);
            var seed = await usage.GetUsageAsync(cancellationToken);
            await ClaudeLimitsWatch.RunAsync(
                seed,
                usage,
                bars.CreateButtonEvents(connection),
                new LimitsDisplay(display),
                TimeSpan.FromSeconds(settings.Interval),
                scheduler,
                logger,
                cancellationToken);
            return 0;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // Ctrl+C, at any point, is the normal way to stop. Any failure seen once it was requested is
            // fallout of it (SIGINT also kills the child `security` process), so it exits silently.
            return 0;
        }
    }

    // Values given as options were validated with the settings, so a failure here comes from the environment.
    private BarConnection ResolveConnection(ClaudeLimitsSettings settings)
    {
        try
        {
            return BarConnection.Resolve(settings.Host, settings.Token, environment);
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            throw new ConfigurationException($"{BarConnection.HostVariable} is not a valid host.");
        }
    }

    // Creating the display touches nothing on the bar; it is where a malformed token is rejected.
    private IBarDisplay CreateDisplay(BarConnection connection)
    {
        try
        {
            return bars.CreateDisplay(connection);
        }
        catch (FormatException)
        {
            throw new ConfigurationException($"{BarConnection.TokenVariable} is not a valid token.");
        }
    }
}
