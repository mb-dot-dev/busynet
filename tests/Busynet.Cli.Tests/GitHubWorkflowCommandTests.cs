using System.Net;
using Busynet.Bar;
using Busynet.Bar.Display;
using Busynet.Cli.Tests.Fakes;
using Busynet.GitHub;
using Busynet.GitHub.Actions;
using Busynet.GitHub.Credentials;
using Busynet.GitHub.Git;
using Busynet.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Busynet.Cli.Tests;

public class GitHubWorkflowCommandTests
{
    private static readonly WorkflowRun Running = new(7, "in_progress", null);

    private sealed record Setup(AppHarness App, FakeBarFactory Bar, FakeGitHubActions Actions, FakeGitCheckout Checkout);

    private static Setup Create(params Func<(WorkflowRun?, int?)>[] polls)
    {
        var app = new AppHarness();
        var bar = new FakeBarFactory();
        var actions = new FakeGitHubActions(polls.Length == 0 ? [() => (Running, null)] : polls);
        var checkout = new FakeGitCheckout();
        app.Services.AddSingleton<IBarFactory>(bar);
        app.Services.AddSingleton<IGitHubActions>(actions);
        app.Services.AddSingleton<IGitCheckout>(checkout);
        return new Setup(app, bar, actions, checkout);
    }

    [Fact]
    public async Task Uploads_icons_clears_draws_then_clears_on_cancel()
    {
        var (app, bar, _, _) = Create(() => (Running, 12));
        using var scope = app;
        bar.Display.OnDraw = _ => app.Cts.Cancel();

        var exit = await app.RunAsync("gh", "workflow", "CI");

        Assert.Equal(0, exit);
        Assert.Equal("", app.Stderr.ToString());
        var calls = bar.Display.Calls;
        Assert.Equal(6, calls.TakeWhile(c => c is FakeBarDisplay.Upload).Count());
        Assert.Equal(FakeBarDisplay.Clear, calls[6]);
        var draw = Assert.IsType<DisplayElements>(calls[7]);
        Assert.Equal(["mb-dot-dev/busynet", "#12 CI"], draw.Elements.OfType<TextElement>().Select(t => t.Text));
        Assert.Equal("in_progress.png", draw.Elements.OfType<ImageElement>().Single().Path);
        Assert.Equal(FakeBarDisplay.Clear, calls[^1]);
    }

    [Fact]
    public async Task Defaults_to_the_checkouts_origin_and_branch()
    {
        var (app, bar, actions, checkout) = Create();
        using var scope = app;
        checkout.Branch = "feature/x";
        checkout.Origin = new GitHubRepo("someone", "else");
        bar.Display.OnDraw = _ => app.Cts.Cancel();

        await app.RunAsync("gh", "workflow", "ci.yaml");

        Assert.Equal("resolve someone/else ci.yaml", actions.Calls[0]);
        Assert.Equal("runs someone/else 161335 feature/x", actions.Calls[1]);
    }

    [Fact]
    public async Task Repo_and_branch_options_skip_the_checkout()
    {
        var (app, bar, actions, checkout) = Create();
        using var scope = app;
        checkout.Failure = new GitException("not a git repository");
        bar.Display.OnDraw = _ => app.Cts.Cancel();

        var exit = await app.RunAsync("gh", "workflow", "CI", "--repo", "octo/cat", "--branch", "dev");

        Assert.Equal(0, exit);
        Assert.Equal(0, checkout.Calls);
        Assert.Equal("runs octo/cat 161335 dev", actions.Calls[1]);
    }

    [Theory]
    [InlineData("octo")]
    [InlineData("octo/")]
    [InlineData("a/b/c")]
    public async Task Malformed_repo_is_a_usage_error_before_anything_runs(string repo)
    {
        var (app, bar, actions, checkout) = Create();
        using var scope = app;

        var exit = await app.RunAsync("gh", "workflow", "CI", "--repo", repo);

        Assert.Equal(2, exit);
        Assert.Contains("--repo must be OWNER/NAME.", app.Stderr.ToString(), StringComparison.Ordinal);
        Assert.Empty(bar.Connections);
        Assert.Empty(actions.Calls);
        Assert.Equal(0, checkout.Calls);
    }

    [Theory]
    [InlineData("gh", "workflow")]
    [InlineData("gh", "workflow", " ")]
    [InlineData("gh", "workflow", "CI", "--branch", " ")]
    [InlineData("gh", "workflow", "CI", "--interval", "0")]
    [InlineData("gh", "workflow", "CI", "--bogus")]
    [InlineData("gh")]
    public async Task Bad_command_lines_exit_2(params string[] args)
    {
        var (app, bar, _, _) = Create();
        using var scope = app;

        var exit = await app.RunAsync(args);

        Assert.Equal(2, exit);
        Assert.NotEqual("", app.Stderr.ToString());
        Assert.Empty(bar.Connections);
    }

    [Fact]
    public async Task Unknown_workflow_prints_one_line_and_exits_1()
    {
        var (app, bar, actions, _) = Create();
        using var scope = app;
        actions.Resolve = () => throw new GitHubException("No workflow matches 'x' in mb-dot-dev/busynet. Available: CI");

        var exit = await app.RunAsync("gh", "workflow", "x");

        Assert.Equal(1, exit);
        Assert.Equal("No workflow matches 'x' in mb-dot-dev/busynet. Available: CI" + Environment.NewLine, app.Stderr.ToString());
        Assert.Empty(bar.Display.Calls);
    }

    [Fact]
    public async Task Missing_token_exits_1_naming_both_options()
    {
        var (app, bar, actions, _) = Create();
        using var scope = app;
        actions.Resolve = () => throw new GitHubAuthException(GitHubTokenSource.NoToken);

        var exit = await app.RunAsync("gh", "workflow", "CI");

        Assert.Equal(1, exit);
        Assert.Equal(GitHubTokenSource.NoToken + Environment.NewLine, app.Stderr.ToString());
        Assert.Empty(bar.Display.Calls);
    }

    [Fact]
    public async Task Git_failure_exits_1()
    {
        var (app, bar, _, checkout) = Create();
        using var scope = app;
        checkout.Failure = new GitException("HEAD is detached; pass --branch explicitly.");

        var exit = await app.RunAsync("gh", "workflow", "CI");

        Assert.Equal(1, exit);
        Assert.Equal("HEAD is detached; pass --branch explicitly." + Environment.NewLine, app.Stderr.ToString());
        Assert.Empty(bar.Display.Calls);
    }

    [Fact]
    public async Task Icon_upload_failure_exits_1_before_drawing()
    {
        var (app, bar, _, _) = Create();
        using var scope = app;
        bar.Display.FailNextUpload(new BarRequestException("POST", "/api/assets/upload", nameof(HttpRequestException)));

        var exit = await app.RunAsync("gh", "workflow", "CI");

        Assert.Equal(1, exit);
        Assert.Equal("request error | POST /api/assets/upload | HttpRequestException" + Environment.NewLine,
            app.Stderr.ToString());
        Assert.DoesNotContain(bar.Display.Calls, c => c is DisplayElements);
    }

    [Fact]
    public async Task Rejected_token_mid_watch_exits_1_after_clearing()
    {
        var (app, bar, _, _) = Create(() => (Running, null), () => throw new GitHubAuthException("GitHub rejected the token (HTTP 401)."));
        using var scope = app;

        var exit = await app.RunAsync("gh", "workflow", "CI", "--interval", "1");

        Assert.Equal(1, exit);
        Assert.Equal("GitHub rejected the token (HTTP 401)." + Environment.NewLine, app.Stderr.ToString());
        Assert.Equal(FakeBarDisplay.Clear, bar.Display.Calls[^1]);
    }

    [Fact]
    public async Task Invalid_host_variable_exits_1_naming_it()
    {
        using var app = new AppHarness(new Dictionary<string, string> { ["BUSYBOY_HOST"] = "http://[bad" });
        app.Services.AddSingleton<IGitHubActions>(new FakeGitHubActions());
        app.Services.AddSingleton<IGitCheckout>(new FakeGitCheckout());

        var exit = await app.RunAsync("gh", "workflow", "CI");

        Assert.Equal(1, exit);
        Assert.Equal("BUSYBOY_HOST is not a valid host." + Environment.NewLine, app.Stderr.ToString());
    }

    [Fact]
    public async Task Invalid_token_variable_exits_1_without_echoing_it()
    {
        using var app = new AppHarness(new Dictionary<string, string> { ["BUSYBOY_TOKEN"] = "bad\ntoken" });
        app.Services.AddSingleton<IGitHubActions>(new FakeGitHubActions());
        app.Services.AddSingleton<IGitCheckout>(new FakeGitCheckout());

        var exit = await app.RunAsync("gh", "workflow", "CI");

        Assert.Equal(1, exit);
        Assert.Equal("BUSYBOY_TOKEN is not a valid token." + Environment.NewLine, app.Stderr.ToString());
    }

    [Fact]
    public async Task Gh_help_lists_workflow()
    {
        using var app = new AppHarness();

        var exit = await app.RunAsync("gh", "--help");

        Assert.Equal(0, exit);
        Assert.Contains("workflow", app.ConsoleOut.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runs_over_the_real_github_and_bar_clients_without_leaking_tokens()
    {
        const string GitHubSecret = "GHSECRET";
        const string BarSecret = "BARSECRET";

        using var app = new AppHarness();
        var http = new FakeHttpHandler();
        http.Add(HttpMethod.Get, "/repos/octo/cat/actions/workflows", HttpStatusCode.OK,
            """{"workflows":[{"id":5,"name":"CI","path":".github/workflows/ci.yaml"}]}""");
        http.Add(HttpMethod.Get, "/repos/octo/cat/actions/workflows/5/runs", HttpStatusCode.OK,
            """{"workflow_runs":[{"id":1,"status":"completed","conclusion":"success"}]}""");
        http.Add(HttpMethod.Get, "/repos/octo/cat/pulls", HttpStatusCode.OK, """[{"number":3}]""");
        http.Add(HttpMethod.Post, "/api/assets/upload", HttpStatusCode.OK, "{}");
        http.Add(HttpMethod.Delete, "/api/display/draw", HttpStatusCode.OK);
        http.Add(HttpMethod.Post, "/api/display/draw", _ =>
        {
            app.Cts.Cancel();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        app.Services.AddHttpClient("busybar").ConfigurePrimaryHttpMessageHandler(() => http);
        app.Services.AddHttpClient("github").ConfigurePrimaryHttpMessageHandler(() => http);
        app.Services.AddSingleton<IGitHubTokenSource>(new FixedTokenSource(GitHubSecret));

        var exit = await app.RunAsync("gh", "workflow", "CI", "--repo", "octo/cat", "--branch", "main",
            "--host", "127.0.0.1:9", "--token", BarSecret, "--verbose");

        Assert.Equal(0, exit);
        var draw = http.Calls.Single(c => c.Method == HttpMethod.Post && c.Uri.AbsolutePath == "/api/display/draw");
        Assert.Contains("\"#3 CI\"", draw.Body, StringComparison.Ordinal);
        Assert.Contains("\"success.png\"", draw.Body, StringComparison.Ordinal);
        Assert.Equal(6, http.Calls.Count(c => c.Uri.AbsolutePath == "/api/assets/upload"));
        Assert.All(http.Calls.Where(c => c.Uri.Host == "api.github.com"),
            c => Assert.Equal($"Bearer {GitHubSecret}", c.Headers["Authorization"]));
        var output = app.Stderr.ToString() + app.ConsoleOut;
        Assert.DoesNotContain(GitHubSecret, output, StringComparison.Ordinal);
        Assert.DoesNotContain(BarSecret, output, StringComparison.Ordinal);
    }

    private sealed class FixedTokenSource(string token) : IGitHubTokenSource
    {
        public Task<string> GetTokenAsync(CancellationToken cancellationToken) => Task.FromResult(token);
    }
}
