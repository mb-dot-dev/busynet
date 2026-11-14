using Busynet.GitHub;
using Busynet.GitHub.Actions;
using Busynet.GitHub.Git;

namespace Busynet.Cli.Tests.Fakes;

/// <summary>
/// Scripted GitHub: a fixed workflow list, and poll results in order (the last repeating). Each poll result is
/// either a snapshot or a failure; the run and pull request of one poll come from the same entry. Records calls.
/// </summary>
public sealed class FakeGitHubActions(params Func<(WorkflowRun? Run, int? PullRequest)>[] polls) : IGitHubActions
{
    private readonly Lock _gate = new();
    private readonly List<string> _calls = [];
    private int _polls;
    private (WorkflowRun? Run, int? PullRequest) _current;

    public static readonly Workflow Ci = new(161335, "CI", ".github/workflows/ci.yaml");

    public Func<Workflow>? Resolve { get; set; }

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate)
                return [.. _calls];
        }
    }

    public int Polls => Volatile.Read(ref _polls);

    public Task<Workflow> ResolveWorkflowAsync(GitHubRepo repo, string reference, CancellationToken ct)
    {
        Record($"resolve {repo.Slug} {reference}");
        try
        {
            return Task.FromResult(Resolve is null ? Ci : Resolve());
        }
        catch (Exception e)
        {
            return Task.FromException<Workflow>(e);
        }
    }

    public Task<WorkflowRun?> GetLatestRunAsync(GitHubRepo repo, long workflowId, string branch, CancellationToken ct)
    {
        Record($"runs {repo.Slug} {workflowId} {branch}");
        var n = Interlocked.Increment(ref _polls);
        try
        {
            _current = polls.Length == 0 ? (null, null) : polls[Math.Min(n, polls.Length) - 1]();
            return Task.FromResult(_current.Run);
        }
        catch (Exception e)
        {
            return Task.FromException<WorkflowRun?>(e);
        }
    }

    public Task<int?> GetPullRequestNumberAsync(GitHubRepo repo, string branch, CancellationToken ct)
    {
        Record($"pulls {repo.Slug} {branch}");
        return Task.FromResult(_current.PullRequest);
    }

    private void Record(string call)
    {
        lock (_gate)
            _calls.Add(call);
    }
}

/// <summary>A checkout on a fixed branch with a fixed origin; either can be made to fail.</summary>
public sealed class FakeGitCheckout : IGitCheckout
{
    public string Branch { get; set; } = "main";

    public GitHubRepo Origin { get; set; } = new("mb-dot-dev", "busynet");

    public GitException? Failure { get; set; }

    public int Calls { get; private set; }

    public Task<string> GetCurrentBranchAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return Failure is null ? Task.FromResult(Branch) : Task.FromException<string>(Failure);
    }

    public Task<GitHubRepo> GetOriginRepoAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return Failure is null ? Task.FromResult(Origin) : Task.FromException<GitHubRepo>(Failure);
    }
}
