using Busynet.Cli.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace Busynet.Cli.Tests;

/// <summary>
/// Runs <see cref="BusynetApp"/> over the real registration with an empty environment, capturing our stderr
/// lines and Spectre's console output. Tests add overrides to <see cref="Services"/> before <see cref="RunAsync"/>.
/// </summary>
public sealed class AppHarness : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public AppHarness(IDictionary<string, string>? environment = null)
    {
        var env = new Dictionary<string, string>(environment ?? new Dictionary<string, string>());
        Services.AddBusynet();
        // Keep the test output clean: tests that look at logs add their own provider.
        Services.RemoveAll<ILoggerProvider>();
        // Never the developer's real BUSYBOY_* variables.
        Services.AddSingleton<Func<string, string?>>((IServiceProvider _) => name => env.GetValueOrDefault(name));
        Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(ConsoleOut),
        });
    }

    public ServiceCollection Services { get; } = new();

    public StringWriter Stderr { get; } = new();

    public StringWriter ConsoleOut { get; } = new();

    public IAnsiConsole Console { get; }

    public CancellationTokenSource Cts { get; } = new();

    public Task<int> RunAsync(params string[] args) =>
        BusynetApp.RunAsync(args, Services, Stderr, Cts.Token, Console).WaitAsync(Timeout);

    public void Dispose()
    {
        Cts.Cancel();
        Cts.Dispose();
        Stderr.Dispose();
        ConsoleOut.Dispose();
    }
}
