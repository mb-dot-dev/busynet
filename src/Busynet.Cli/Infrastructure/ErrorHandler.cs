using Busynet.Core;
using Spectre.Console.Cli;

namespace Busynet.Cli.Infrastructure;

/// <summary>
/// The exit-code contract: usage errors exit 2, expected failures print their one-line message and exit 1
/// (the full exception with <c>--verbose</c>), anything else prints in full and exits 1. Ctrl+C exits 0.
/// </summary>
public sealed class ErrorHandler(TextWriter stderr, CancellationToken cancellationToken)
{
    public const int Failure = 1;
    public const int UsageError = 2;

    /// <param name="resolver">The run's services; null when the failure came before the command (parsing).</param>
    public int Handle(Exception exception, ITypeResolver? resolver)
    {
        ArgumentNullException.ThrowIfNull(exception);

        switch (exception)
        {
            case OperationCanceledException when cancellationToken.IsCancellationRequested:
                return 0;
            case CommandAppException:
                stderr.WriteLine(exception.Message);
                return UsageError;
            case BusynetException:
                stderr.WriteLine(IsVerbose(resolver) ? exception.ToString() : exception.Message);
                return Failure;
            default:
                stderr.WriteLine(exception.ToString());
                return Failure;
        }
    }

    private static bool IsVerbose(ITypeResolver? resolver) =>
        resolver?.Resolve(typeof(Verbosity)) is Verbosity { IsVerbose: true };
}
