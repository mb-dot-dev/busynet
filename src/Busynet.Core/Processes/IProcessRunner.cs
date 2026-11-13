namespace Busynet.Core.Processes;

public interface IProcessRunner
{
    /// <summary>
    /// Runs a process and captures its standard output. Standard error is discarded.
    /// </summary>
    /// <exception cref="ProcessRunFailedException">The process could not start or exceeded <paramref name="timeout"/>.</exception>
    Task<ProcessOutcome> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
