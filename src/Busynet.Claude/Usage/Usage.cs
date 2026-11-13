namespace Busynet.Claude.Usage;

/// <summary>One usage limit: a 0-100 utilization and when the window resets (null when the API gives none).</summary>
public sealed record UsageWindow(double Utilization, DateTimeOffset? ResetsAt);

/// <summary>The two limits displayed; every other field the endpoint returns is ignored.</summary>
public sealed record Usage(UsageWindow Session, UsageWindow Weekly);
