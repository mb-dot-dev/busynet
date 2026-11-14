using Busynet.GitHub.Credentials;

namespace Busynet.GitHub.Tests.Credentials;

public sealed class GitHubTokenSourceTests
{
    private const string GhAuthToken = "gh auth token";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static GitHubTokenSource Create(ScriptedRunner runner, string? envToken = null) =>
        new(runner, name => name == GitHubTokenSource.TokenVariable ? envToken : null);

    [Fact]
    public async Task Prefers_the_gh_token()
    {
        var token = await Create(new ScriptedRunner().On(GhAuthToken, 0, "gho_abc\n"), "env").GetTokenAsync(Ct);

        Assert.Equal("gho_abc", token);
    }

    [Fact]
    public async Task Falls_back_to_GITHUB_TOKEN_when_gh_is_not_logged_in()
    {
        var token = await Create(new ScriptedRunner().On(GhAuthToken, 1), " env-token ").GetTokenAsync(Ct);

        Assert.Equal("env-token", token);
    }

    [Fact]
    public async Task Falls_back_to_GITHUB_TOKEN_when_gh_is_missing()
    {
        var token = await Create(new ScriptedRunner().Failing(GhAuthToken), "env-token").GetTokenAsync(Ct);

        Assert.Equal("env-token", token);
    }

    [Fact]
    public async Task Falls_back_when_gh_prints_nothing()
    {
        var token = await Create(new ScriptedRunner().On(GhAuthToken, 0, "  \n"), "env-token").GetTokenAsync(Ct);

        Assert.Equal("env-token", token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Neither_is_an_auth_error_naming_both(string? envToken)
    {
        var e = await Assert.ThrowsAsync<GitHubAuthException>(
            () => Create(new ScriptedRunner().On(GhAuthToken, 1), envToken).GetTokenAsync(Ct));

        Assert.Equal(GitHubTokenSource.NoToken, e.Message);
    }
}
