namespace Busynet.GitHub;

/// <summary>A GitHub repository, identified the way the REST API identifies one.</summary>
public sealed record GitHubRepo(string Owner, string Name)
{
    /// <summary>The <c>owner/name</c> form used in API paths and on the display.</summary>
    public string Slug => $"{Owner}/{Name}";

    /// <summary>Parses exactly <c>owner/name</c>: both parts non-empty, one slash.</summary>
    public static bool TryParse(string? value, out GitHubRepo? repo)
    {
        repo = null;
        var parts = value?.Split('/');
        if (parts is not [{ Length: > 0 } owner, { Length: > 0 } name]
            || owner.Any(char.IsWhiteSpace) || name.Any(char.IsWhiteSpace))
            return false;

        repo = new GitHubRepo(owner, name);
        return true;
    }
}
