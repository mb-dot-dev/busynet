using System.CommandLine;
using System.Reactive.Concurrency;
using Busynet.Bar;
using Busynet.Cli.Infrastructure;
using Busynet.GitHub.Actions;
using Busynet.GitHub.Git;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Busynet.Cli.Features.GitHubWorkflow;

/// <summary>
/// <c>busynet gh workflow</c>: resolves the repository, branch, token and workflow, then uploads the status icons
/// (any failure so far ends the command with exit 1), then the watch, which clears, draws and keeps polling until
/// Ctrl+C.
/// </summary>
public sealed class GitHubWorkflowCommand(
    IBarFactory bars,
    IGitHubActions actions,
    IGitCheckout checkout,
    IScheduler scheduler,
    Func<string, string?> environment,
    ILogger<GitHubWorkflowCommand> logger)
{
    public const string Description = "Watch a GitHub Actions workflow's latest run on the bar until Ctrl+C.";

    /// <summary>The <c>workflow</c> command: parses the options, then runs <see cref="ExecuteAsync"/> from <paramref name="provider"/>.</summary>
    public static Command Define(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var options = new GitHubWorkflowOptions(new BarOptions());
        var command = new Command("workflow", Description);
        options.AddTo(command);
        command.SetAction((parse, ct) =>
        {
            var settings = options.Read(parse);
            // Before the command resolves: the logging filter and the error handler read it.
            provider.GetRequiredService<Verbosity>().IsVerbose = settings.Verbose;
            return provider.GetRequiredService<GitHubWorkflowCommand>().ExecuteAsync(settings, ct);
        });
        return command;
    }

    public async Task<int> ExecuteAsync(GitHubWorkflowSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var (_, bar) = BarConnector.Connect(bars, settings, environment);
            var repo = settings.Repo ?? await checkout.GetOriginRepoAsync(cancellationToken);
            var branch = settings.Branch ?? await checkout.GetCurrentBranchAsync(cancellationToken);
            var workflow = await actions.ResolveWorkflowAsync(repo, settings.Workflow, cancellationToken);

            var display = new WorkflowDisplay(bar);
            await display.UploadIconsAsync(cancellationToken);
            await GitHubWorkflowWatch.RunAsync(
                new WorkflowTarget(repo, branch, workflow),
                actions,
                display,
                TimeSpan.FromSeconds(settings.Interval),
                scheduler,
                logger,
                cancellationToken);
            return 0;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // Ctrl+C, at any point, is the normal way to stop. Any failure seen once it was requested is
            // fallout of it (SIGINT also kills a child `git` or `gh` process), so it exits silently.
            return 0;
        }
    }
}
