using System.Reactive.Concurrency;
using Busynet.Bar;
using Busynet.Claude;
using Busynet.Cli.Features.ClaudeLimits;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Busynet.Cli.Infrastructure;

public static class ServiceRegistration
{
    /// <summary>Everything the commands need: the bar, Claude, logging to stderr, the clock and the environment.</summary>
    public static IServiceCollection AddBusynet(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // URLs and headers carry tokens: no HTTP client may log them.
        services.ConfigureHttpClientDefaults(b => b.RemoveAllLoggers());
        services.AddBusyBar();
        services.AddClaude();

        services.TryAddSingleton<Verbosity>();
        services.AddTransient<ClaudeLimitsCommand>();
        services.AddLogging(b =>
        {
            b.ClearProviders();
            b.SetMinimumLevel(LogLevel.Trace);
            b.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
            });
        });
        // Everything goes to stderr: stdout is for command output only.
        services.Configure<ConsoleLoggerOptions>(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        // Read at log time, so --verbose takes effect though the loggers exist before it is parsed.
        services.AddSingleton<IConfigureOptions<LoggerFilterOptions>>(sp =>
        {
            var verbosity = sp.GetRequiredService<Verbosity>();
            return new ConfigureOptions<LoggerFilterOptions>(o =>
                o.AddFilter((_, _, level) => level >= (verbosity.IsVerbose ? LogLevel.Debug : LogLevel.Warning)));
        });

        services.TryAddSingleton<IScheduler>(DefaultScheduler.Instance);
        services.TryAddSingleton<Func<string, string?>>((IServiceProvider _) => Environment.GetEnvironmentVariable);
        return services;
    }
}
