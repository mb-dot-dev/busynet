namespace Busynet.GitHub.Actions;

/// <summary>One Actions workflow defined in the repository.</summary>
public sealed record Workflow(long Id, string Name, string Path);

/// <summary>One run of a workflow. <c>Conclusion</c> is null until the run completes.</summary>
public sealed record WorkflowRun(long Id, string Status, string? Conclusion);
