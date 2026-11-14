namespace Busynet.Bar.Layouts;

/// <summary>
/// One workflow as drawn: the repository on top, the pull request or branch (and workflow name) below, and the
/// run's status icon. Compared by value, so equal statuses mean equal payloads.
/// </summary>
public sealed record WorkflowStatus(string RepoLabel, string RefLabel, WorkflowIcon Icon);
