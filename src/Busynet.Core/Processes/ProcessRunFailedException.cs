namespace Busynet.Core.Processes;

public sealed class ProcessRunFailedException(string message) : Exception(message);
