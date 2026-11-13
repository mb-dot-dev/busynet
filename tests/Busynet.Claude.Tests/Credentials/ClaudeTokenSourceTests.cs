using Busynet.Claude.Credentials;
using Busynet.Core.Processes;
using Microsoft.Extensions.Time.Testing;

namespace Busynet.Claude.Tests.Credentials;

public sealed class ClaudeTokenSourceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly long NowMs = Now.ToUnixTimeMilliseconds();

    private readonly string _dir = Directory.CreateTempSubdirectory("busynet-claude-").FullName;
    private readonly FakeTimeProvider _time = new(Now);
    private readonly FakeRunner _runner = new();

    private string FilePath => Path.Combine(_dir, ".credentials.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ClaudeTokenSource Source(bool useKeychain = true) =>
        new(_runner, new ClaudeCredentialOptions(useKeychain, FilePath), _time);

    private static string Blob(string token, string? expiresAt = null)
    {
        var expiry = expiresAt is null ? "" : ",\"expiresAt\":" + expiresAt;
        return "{\"claudeAiOauth\":{\"accessToken\":\"" + token + "\"" + expiry + "}}";
    }

    [Fact]
    public async Task Keychain_token_is_used()
    {
        _runner.Result = new ProcessOutcome(0, Blob("kc-token", (NowMs + 60_000).ToString()) + "\n");

        Assert.Equal("kc-token", await Source().GetTokenAsync(CancellationToken.None));
        Assert.Equal("security", _runner.FileName);
        Assert.Equal(["find-generic-password", "-s", "Claude Code-credentials", "-w"], _runner.Arguments);
        Assert.Equal(TimeSpan.FromSeconds(10), _runner.Timeout);
    }

    [Fact]
    public async Task Missing_keychain_entry_falls_back_to_file()
    {
        _runner.Result = new ProcessOutcome(44, "");
        File.WriteAllText(FilePath, Blob("file-token"));

        Assert.Equal("file-token", await Source().GetTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Failing_security_command_falls_back_to_file()
    {
        _runner.Result = new ProcessOutcome(1, "");
        File.WriteAllText(FilePath, Blob("file-token"));

        Assert.Equal("file-token", await Source().GetTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Keychain_timeout_falls_back_to_file()
    {
        _runner.Throw = new ProcessRunFailedException("timed out");
        File.WriteAllText(FilePath, Blob("file-token"));

        Assert.Equal("file-token", await Source().GetTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Expired_keychain_token_does_not_fall_through()
    {
        _runner.Result = new ProcessOutcome(0, Blob("kc-token", NowMs.ToString()));
        File.WriteAllText(FilePath, Blob("file-token"));

        var e = await Assert.ThrowsAsync<ClaudeAuthException>(() => Source().GetTokenAsync(CancellationToken.None));
        Assert.Equal("The Claude Code token has expired. Run `claude` to refresh it.", e.Message);
    }

    [Fact]
    public async Task Expired_file_token_is_auth_error()
    {
        _runner.Result = new ProcessOutcome(44, "");
        File.WriteAllText(FilePath, Blob("file-token", (NowMs - 1).ToString()));

        var e = await Assert.ThrowsAsync<ClaudeAuthException>(() => Source().GetTokenAsync(CancellationToken.None));
        Assert.Equal("The Claude Code token has expired. Run `claude` to refresh it.", e.Message);
    }

    [Fact]
    public async Task No_credentials_anywhere_is_auth_error()
    {
        _runner.Result = new ProcessOutcome(44, "");

        var e = await Assert.ThrowsAsync<ClaudeAuthException>(() => Source().GetTokenAsync(CancellationToken.None));
        Assert.Equal("No Claude Code credentials found. Run `claude` to log in.", e.Message);
    }

    [Fact]
    public async Task Blank_keychain_output_counts_as_no_blob()
    {
        _runner.Result = new ProcessOutcome(0, "  \n");

        await Assert.ThrowsAsync<ClaudeAuthException>(() => Source().GetTokenAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task Keychain_failure_without_file_is_transient(int mode)
    {
        if (mode > 0)
            _runner.Result = new ProcessOutcome(1, "");
        else
            _runner.Throw = new ProcessRunFailedException("timed out");

        var e = await Assert.ThrowsAsync<ClaudeTransientException>(() => Source().GetTokenAsync(CancellationToken.None));
        Assert.Equal("Could not read the Claude Code credentials from the keychain.", e.Message);
        Assert.Null(e.RetryAfter);
    }

    [Fact]
    public async Task Non_keychain_platform_reads_file_only()
    {
        File.WriteAllText(FilePath, Blob("file-token"));

        Assert.Equal("file-token", await Source(useKeychain: false).GetTokenAsync(CancellationToken.None));
        Assert.Equal(0, _runner.Calls);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"claudeAiOauth":{}}""")]
    [InlineData("""{"claudeAiOauth":{"accessToken":""}}""")]
    public async Task Malformed_json_is_unreadable(string json)
    {
        _runner.Result = new ProcessOutcome(0, json);

        var e = await Assert.ThrowsAsync<ClaudeAuthException>(() => Source().GetTokenAsync(CancellationToken.None));
        Assert.Equal("Claude Code credentials are unreadable. Run `claude` to log in.", e.Message);
    }

    [Theory]
    [InlineData("\"soon\"")]
    [InlineData("true")]
    public async Task Non_numeric_expires_at_is_unreadable(string expiresAt)
    {
        _runner.Result = new ProcessOutcome(0, Blob("kc-token", expiresAt));

        var e = await Assert.ThrowsAsync<ClaudeAuthException>(() => Source().GetTokenAsync(CancellationToken.None));
        Assert.Equal("Claude Code credentials are unreadable. Run `claude` to log in.", e.Message);
    }

    [Fact]
    public async Task Null_expires_at_is_accepted()
    {
        _runner.Result = new ProcessOutcome(0, Blob("kc-token", "null"));

        Assert.Equal("kc-token", await Source().GetTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Directory_at_credentials_path_is_treated_as_no_file()
    {
        _runner.Result = new ProcessOutcome(44, "");
        Directory.CreateDirectory(FilePath);

        var e = await Assert.ThrowsAsync<ClaudeAuthException>(() => Source().GetTokenAsync(CancellationToken.None));
        Assert.Equal("No Claude Code credentials found. Run `claude` to log in.", e.Message);
    }

    [Fact]
    public async Task Missing_expires_at_is_accepted()
    {
        _runner.Result = new ProcessOutcome(0, Blob("kc-token"));

        Assert.Equal("kc-token", await Source().GetTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Non_utf8_file_is_unreadable()
    {
        _runner.Result = new ProcessOutcome(44, "");
        File.WriteAllBytes(FilePath, [0xFF, 0xFE, 0xFD]);

        var e = await Assert.ThrowsAsync<ClaudeAuthException>(() => Source().GetTokenAsync(CancellationToken.None));
        Assert.Equal("Claude Code credentials are unreadable. Run `claude` to log in.", e.Message);
    }

    [Fact]
    public async Task Messages_never_contain_the_blob()
    {
        const string Secret = "sk-ant-oat01-SECRET";
        var blobs = new[]
        {
            Blob(Secret, (NowMs - 1).ToString()),
            Blob(Secret, "\"soon\""),
            $$"""{"claudeAiOauth":{"accessToken":"{{Secret}}","expiresAt":[}""",
            $$"""{"other":"{{Secret}}"}""",
        };

        foreach (var blob in blobs)
        {
            _runner.Result = new ProcessOutcome(0, blob);
            var e = await Assert.ThrowsAsync<ClaudeAuthException>(() => Source().GetTokenAsync(CancellationToken.None));
            Assert.DoesNotContain(Secret, e.ToString());
        }
    }

    private sealed class FakeRunner : IProcessRunner
    {
        public ProcessOutcome Result { get; set; } = new(44, "");
        public Exception? Throw { get; set; }
        public int Calls { get; private set; }
        public string? FileName { get; private set; }
        public IReadOnlyList<string>? Arguments { get; private set; }
        public TimeSpan Timeout { get; private set; }

        public Task<ProcessOutcome> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Calls++;
            FileName = fileName;
            Arguments = arguments;
            Timeout = timeout;
            return Throw is null ? Task.FromResult(Result) : Task.FromException<ProcessOutcome>(Throw);
        }
    }
}
