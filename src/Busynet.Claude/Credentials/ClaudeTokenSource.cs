using System.Text;
using System.Text.Json;
using Busynet.Core.Processes;

namespace Busynet.Claude.Credentials;

public sealed class ClaudeTokenSource(IProcessRunner runner, ClaudeCredentialOptions options, TimeProvider time)
    : IClaudeTokenSource
{
    private const string KeychainService = "Claude Code-credentials";
    private const int KeychainItemNotFound = 44;
    private static readonly TimeSpan KeychainTimeout = TimeSpan.FromSeconds(10);

    private const string NoCredentials = "No Claude Code credentials found. Run `claude` to log in.";
    private const string Unreadable = "Claude Code credentials are unreadable. Run `claude` to log in.";
    private const string Expired = "The Claude Code token has expired. Run `claude` to refresh it.";
    private const string KeychainFailed = "Could not read the Claude Code credentials from the keychain.";

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        var keychainFailed = false;
        string? blob = null;

        if (options.UseKeychain)
            (blob, keychainFailed) = await ReadKeychainAsync(cancellationToken);

        blob ??= ReadFile();

        if (blob is null)
            throw keychainFailed ? new ClaudeTransientException(KeychainFailed) : new ClaudeAuthException(NoCredentials);

        return ExtractToken(blob);
    }

    private async Task<(string? Blob, bool Failed)> ReadKeychainAsync(CancellationToken ct)
    {
        try
        {
            var outcome = await runner.RunAsync(
                "security", ["find-generic-password", "-s", KeychainService, "-w"], KeychainTimeout, ct);

            return outcome.ExitCode switch
            {
                0 when !string.IsNullOrWhiteSpace(outcome.StandardOutput) => (outcome.StandardOutput, false),
                0 or KeychainItemNotFound => (null, false),
                _ => (null, true),
            };
        }
        catch (ProcessRunFailedException)
        {
            return (null, true);
        }
    }

    private string? ReadFile()
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(options.CredentialsFilePath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new ClaudeAuthException(Unreadable);
        }
    }

    private string ExtractToken(string blob)
    {
        try
        {
            using var doc = JsonDocument.Parse(blob);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth)
                || oauth.ValueKind != JsonValueKind.Object
                || !oauth.TryGetProperty("accessToken", out var tokenElement)
                || tokenElement.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(tokenElement.GetString()))
                throw new ClaudeAuthException(Unreadable);

            if (oauth.TryGetProperty("expiresAt", out var expiresAt) && expiresAt.ValueKind != JsonValueKind.Null)
            {
                if (expiresAt.ValueKind != JsonValueKind.Number)
                    throw new ClaudeAuthException(Unreadable);
                if (expiresAt.GetDouble() <= time.GetUtcNow().ToUnixTimeMilliseconds())
                    throw new ClaudeAuthException(Expired);
            }

            return tokenElement.GetString()!;
        }
        catch (JsonException)
        {
            throw new ClaudeAuthException(Unreadable);
        }
    }
}
