namespace Busynet.Core.Processes;

public sealed record ProcessOutcome(int ExitCode, string StandardOutput);
