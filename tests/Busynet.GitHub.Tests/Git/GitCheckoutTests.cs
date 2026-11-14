using Busynet.GitHub.Git;

namespace Busynet.GitHub.Tests.Git;

public sealed class GitCheckoutTests
{
    private const string RevParse = "git rev-parse --abbrev-ref HEAD";
    private const string GetUrl = "git remote get-url origin";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("git@github.com:mb-dot-dev/busynet.git")]
    [InlineData("git@github.com:mb-dot-dev/busynet")]
    [InlineData("https://github.com/mb-dot-dev/busynet")]
    [InlineData("https://github.com/mb-dot-dev/busynet.git")]
    [InlineData("https://github.com/mb-dot-dev/busynet/")]
    [InlineData("ssh://git@github.com/mb-dot-dev/busynet.git")]
    [InlineData("https://user:secret@github.com/mb-dot-dev/busynet.git")]
    public void Parses_every_remote_url_form(string url)
    {
        Assert.Equal(new GitHubRepo("mb-dot-dev", "busynet"), GitCheckout.ParseRemoteUrl(url));
    }

    [Fact]
    public void Unreadable_url_is_reported_without_credentials()
    {
        var e = Assert.Throws<GitException>(() => GitCheckout.ParseRemoteUrl("https://user:secret@example.com"));

        Assert.DoesNotContain("secret", e.Message, StringComparison.Ordinal);
        Assert.Contains("<redacted>", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reads_the_current_branch()
    {
        var checkout = new GitCheckout(new ScriptedRunner().On(RevParse, 0, "feature/x\n"));

        Assert.Equal("feature/x", await checkout.GetCurrentBranchAsync(Ct));
    }

    [Fact]
    public async Task Detached_head_asks_for_branch()
    {
        var checkout = new GitCheckout(new ScriptedRunner().On(RevParse, 0, "HEAD\n"));

        var e = await Assert.ThrowsAsync<GitException>(() => checkout.GetCurrentBranchAsync(Ct));

        Assert.Contains("--branch", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reads_the_origin_repo()
    {
        var checkout = new GitCheckout(new ScriptedRunner().On(GetUrl, 0, "git@github.com:mb-dot-dev/busynet.git\n"));

        Assert.Equal(new GitHubRepo("mb-dot-dev", "busynet"), await checkout.GetOriginRepoAsync(Ct));
    }

    [Fact]
    public async Task Not_a_repository_is_a_git_error()
    {
        var checkout = new GitCheckout(new ScriptedRunner().On(GetUrl, 128));

        var e = await Assert.ThrowsAsync<GitException>(() => checkout.GetOriginRepoAsync(Ct));

        Assert.StartsWith("git remote get-url origin failed (exit 128).", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_git_is_a_git_error()
    {
        var checkout = new GitCheckout(new ScriptedRunner().Failing(RevParse));

        await Assert.ThrowsAsync<GitException>(() => checkout.GetCurrentBranchAsync(Ct));
    }
}
