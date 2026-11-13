using System.Diagnostics;
using Busynet.Core.Processes;

namespace Busynet.Core.Tests.Processes;

[Trait("os", "unix")]
public sealed class ProcessRunnerTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);

    private static Task<ProcessOutcome> RunSh(string script, TimeSpan timeout)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix-only test");
        return new ProcessRunner().RunAsync("sh", ["-c", script], timeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Captures_stdout_and_exit_code()
    {
        var outcome = await RunSh("printf hi; exit 44", Generous);

        Assert.Equal(44, outcome.ExitCode);
        Assert.Equal("hi", outcome.StandardOutput);
    }

    [Fact]
    public async Task Stderr_is_not_captured()
    {
        var outcome = await RunSh("echo secret 1>&2", Generous);

        Assert.Equal("", outcome.StandardOutput);
    }

    [Fact]
    public async Task Timeout_throws_ProcessRunFailed()
    {
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAsync<ProcessRunFailedException>(
            () => RunSh("exec sleep 5", TimeSpan.FromMilliseconds(200)));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task Missing_executable_throws_ProcessRunFailed()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix-only test");

        await Assert.ThrowsAsync<ProcessRunFailedException>(
            () => new ProcessRunner().RunAsync(
                "definitely-not-a-binary-xyz", [], Generous, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Macos_security_exits_44_for_an_item_that_does_not_exist()
    {
        // Pins the "keychain item not found = 44" assumption without reading any real credential.
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "macOS-only test");

        var outcome = await new ProcessRunner().RunAsync(
            "security", ["find-generic-password", "-s", $"busynet-test-{Guid.NewGuid()}", "-w"],
            Generous, TestContext.Current.CancellationToken);

        Assert.Equal(44, outcome.ExitCode);
    }
}
