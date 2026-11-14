using Busynet.Core.Processes;

namespace Busynet.GitHub.Tests;

/// <summary>Answers each command line (<c>"git remote get-url origin"</c>) from a script, recording every call.</summary>
public sealed class ScriptedRunner : IProcessRunner
{
    private readonly Dictionary<string, Func<ProcessOutcome>> _script = [];

    public List<string> Calls { get; } = [];

    public ScriptedRunner On(string commandLine, int exitCode, string stdout = "")
    {
        _script[commandLine] = () => new ProcessOutcome(exitCode, stdout);
        return this;
    }

    public ScriptedRunner Failing(string commandLine)
    {
        _script[commandLine] = () => throw new ProcessRunFailedException($"Could not run '{commandLine}'.");
        return this;
    }

    public Task<ProcessOutcome> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var line = string.Join(' ', [fileName, .. arguments]);
        Calls.Add(line);
        return _script.TryGetValue(line, out var outcome)
            ? Task.FromResult(outcome())
            : throw new ProcessRunFailedException($"Could not run '{fileName}'.");
    }
}
