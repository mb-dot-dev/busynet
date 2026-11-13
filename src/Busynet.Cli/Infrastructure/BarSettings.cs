namespace Busynet.Cli.Infrastructure;

/// <summary>Options shared by every command that talks to the bar.</summary>
public record BarSettings(string? Host, string? Token, bool Verbose);
