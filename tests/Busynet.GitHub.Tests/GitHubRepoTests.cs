namespace Busynet.GitHub.Tests;

public sealed class GitHubRepoTests
{
    [Fact]
    public void Parses_owner_and_name()
    {
        Assert.True(GitHubRepo.TryParse("mb-dot-dev/busynet", out var repo));
        Assert.Equal(new GitHubRepo("mb-dot-dev", "busynet"), repo);
        Assert.Equal("mb-dot-dev/busynet", repo!.Slug);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("busynet")]
    [InlineData("/busynet")]
    [InlineData("mb-dot-dev/")]
    [InlineData("a/b/c")]
    [InlineData("a b/c")]
    public void Rejects_anything_but_owner_slash_name(string? value)
    {
        Assert.False(GitHubRepo.TryParse(value, out var repo));
        Assert.Null(repo);
    }
}
