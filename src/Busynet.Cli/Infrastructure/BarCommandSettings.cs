using System.ComponentModel;
using Busynet.Bar;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Busynet.Cli.Infrastructure;

/// <summary>Options shared by every command that talks to the bar.</summary>
public class BarCommandSettings : CommandSettings
{
    [CommandOption("--host <HOST>")]
    [Description("Bar host, host:port or URL. Default: $BUSYBOY_HOST, else 10.0.4.20.")]
    public string? Host { get; set; }

    [CommandOption("--token <TOKEN>")]
    [Description("Bar API token. Default: $BUSYBOY_TOKEN; none when unset.")]
    public string? Token { get; set; }

    [CommandOption("--verbose")]
    [Description("Debug logging and full error details on stderr.")]
    public bool Verbose { get; set; }

    /// <summary>Rejects unusable <c>--host</c> / <c>--token</c> values without echoing them.</summary>
    public override ValidationResult Validate()
    {
        if (!string.IsNullOrWhiteSpace(Host) && !IsValidHost(Host))
            return ValidationResult.Error("Invalid --host value.");
        if (!string.IsNullOrWhiteSpace(Token) && !IsValidToken(Token))
            return ValidationResult.Error("Invalid --token value.");
        return ValidationResult.Success();
    }

    private static bool IsValidHost(string host)
    {
        try
        {
            BarConnection.Resolve(host, null, _ => null);
            return true;
        }
        catch (Exception e) when (e is FormatException or ArgumentException)
        {
            return false;
        }
    }

    // The same check the bar's HTTP client applies when the token becomes its X-API-Token header.
    private static bool IsValidToken(string token)
    {
        using var request = new HttpRequestMessage();
        try
        {
            request.Headers.Add("X-API-Token", token.Trim());
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
