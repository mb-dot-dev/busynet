namespace Busynet.Claude.Credentials;

public sealed record ClaudeCredentialOptions(bool UseKeychain, string CredentialsFilePath)
{
    public static ClaudeCredentialOptions Default => new(
        OperatingSystem.IsMacOS(),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json"));
}
