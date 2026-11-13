using System.CommandLine;
using Busynet.Bar;

namespace Busynet.Cli.Infrastructure;

/// <summary>The <c>--host</c>, <c>--token</c> and <c>--verbose</c> options every bar command takes.</summary>
public sealed class BarOptions
{
    public BarOptions()
    {
        Host = new Option<string?>("--host")
        {
            Description = "Bar host, host:port or URL. Default: $BUSYBOY_HOST, else 10.0.4.20.",
        };
        Host.Validators.Add(result =>
        {
            var host = result.GetValueOrDefault<string?>();
            if (!string.IsNullOrWhiteSpace(host) && !IsValidHost(host))
                result.AddError("Invalid --host value.");
        });

        Token = new Option<string?>("--token")
        {
            Description = "Bar API token. Default: $BUSYBOY_TOKEN; none when unset.",
        };
        Token.Validators.Add(result =>
        {
            var token = result.GetValueOrDefault<string?>();
            if (!string.IsNullOrWhiteSpace(token) && !IsValidToken(token))
                result.AddError("Invalid --token value.");
        });

        Verbose = new Option<bool>("--verbose")
        {
            Description = "Debug logging and full error details on stderr.",
        };
    }

    public Option<string?> Host { get; }

    public Option<string?> Token { get; }

    public Option<bool> Verbose { get; }

    public void AddTo(Command command)
    {
        ArgumentNullException.ThrowIfNull(command);

        command.Options.Add(Host);
        command.Options.Add(Token);
        command.Options.Add(Verbose);
    }

    public BarSettings Read(ParseResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new BarSettings(result.GetValue(Host), result.GetValue(Token), result.GetValue(Verbose));
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
