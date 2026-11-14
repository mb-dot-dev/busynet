using System.ComponentModel;
using System.Diagnostics;

namespace Busynet.Core.Processes;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<ProcessOutcome> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            var result = await Process.RunAndCaptureTextAsync(fileName, arguments, timeoutSource.Token);
            if (result.ExitStatus.Canceled)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw TimedOut(fileName, timeout);
            }

            return new ProcessOutcome(result.ExitStatus.ExitCode, result.StandardOutput);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw TimedOut(fileName, timeout);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            throw new ProcessRunFailedException($"Could not run '{fileName}'.");
        }
    }

    private static ProcessRunFailedException TimedOut(string fileName, TimeSpan timeout) =>
        new($"'{fileName}' did not finish within {timeout.TotalSeconds:0.###}s.");
}
