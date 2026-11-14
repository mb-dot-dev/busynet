using System.CommandLine;
using Busynet.Cli.Infrastructure;
using Busynet.GitHub;

namespace Busynet.Cli.Features.GitHubWorkflow;

/// <summary>
/// The arguments of <c>busynet gh workflow</c>: <c>WORKFLOW</c>, <c>--branch</c>, <c>--repo</c>, <c>--interval</c> and the
/// shared bar options. A malformed <c>--repo</c> is a usage error (exit 2) whatever the environment.
/// </summary>
public sealed class GitHubWorkflowOptions
{
    private readonly BarOptions _bar;

    public GitHubWorkflowOptions(BarOptions bar)
    {
        ArgumentNullException.ThrowIfNull(bar);

        _bar = bar;
        Workflow = new Argument<string>("WORKFLOW")
        {
            Description = "Workflow id, file name (ci.yaml) or display name (CI).",
        };
        Workflow.Validators.Add(result =>
        {
            if (string.IsNullOrWhiteSpace(result.GetValueOrDefault<string>()))
                result.AddError("WORKFLOW must not be empty.");
        });

        Branch = new Option<string?>("--branch")
        {
            Description = "Branch to watch. Default: the current checkout's branch.",
        };
        Branch.Validators.Add(result =>
        {
            if (result.Tokens.Count > 0 && string.IsNullOrWhiteSpace(result.Tokens[0].Value))
                result.AddError("--branch must not be empty.");
        });

        Repo = new Option<string?>("--repo")
        {
            Description = "Repository as OWNER/NAME. Default: the origin remote's.",
            HelpName = "OWNER/NAME",
        };
        Repo.Validators.Add(result =>
        {
            if (result.Tokens.Count > 0 && !GitHubRepo.TryParse(result.Tokens[0].Value, out _))
                result.AddError("--repo must be OWNER/NAME.");
        });

        Interval = new Option<int>("--interval")
        {
            Description = "Seconds between polls.",
            DefaultValueFactory = _ => GitHubWorkflowSettings.DefaultInterval,
        };
        Interval.Validators.Add(result =>
        {
            // Read the raw token: the typed value throws when the conversion itself failed, and that
            // failure is already reported as a parse error.
            if (result.Tokens.Count > 0 && int.TryParse(result.Tokens[0].Value, out var seconds) && seconds < 1)
                result.AddError("--interval must be at least 1 second.");
        });
    }

    public Argument<string> Workflow { get; }

    public Option<string?> Branch { get; }

    public Option<string?> Repo { get; }

    public Option<int> Interval { get; }

    public void AddTo(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        command.Arguments.Add(Workflow);
        command.Options.Add(Branch);
        command.Options.Add(Repo);
        command.Options.Add(Interval);
        _bar.AddTo(command);
    }

    public GitHubWorkflowSettings Read(ParseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var bar = _bar.Read(result);
        // Validated while parsing: a value that does not parse is absent.
        var repo = GitHubRepo.TryParse(result.GetValue(Repo), out var parsed) ? parsed : null;
        return new GitHubWorkflowSettings(
            bar.Host,
            bar.Token,
            bar.Verbose,
            result.GetValue(Workflow)!.Trim(),
            result.GetValue(Branch)?.Trim(),
            repo,
            result.GetValue(Interval));
    }
}
