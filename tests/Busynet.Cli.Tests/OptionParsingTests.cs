using System.CommandLine;
using Busynet.Cli.Features.ClaudeLimits;

namespace Busynet.Cli.Tests;

public class OptionParsingTests
{
    private static (ClaudeLimitsOptions Options, ParseResult Parse) Parse(params string[] args)
    {
        var options = new ClaudeLimitsOptions(new Busynet.Cli.Infrastructure.BarOptions());
        var command = new Command("t");
        options.AddTo(command);
        return (options, command.Parse(args));
    }

    [Fact]
    public void Defaults_are_null_host_null_token_not_verbose_interval_180()
    {
        var (options, parse) = Parse();

        Assert.Empty(parse.Errors);
        var (bar, interval) = options.Read(parse);
        Assert.Null(bar.Host);
        Assert.Null(bar.Token);
        Assert.False(bar.Verbose);
        Assert.Equal(180, interval);
    }

    [Fact]
    public void Reads_all_options()
    {
        var (options, parse) = Parse("--host", "10.0.0.1:8080", "--token", "abc", "--verbose", "--interval", "5");

        Assert.Empty(parse.Errors);
        var (bar, interval) = options.Read(parse);
        Assert.Equal("10.0.0.1:8080", bar.Host);
        Assert.Equal("abc", bar.Token);
        Assert.True(bar.Verbose);
        Assert.Equal(5, interval);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Interval_below_one_is_error_with_exact_message(string value)
    {
        var (_, parse) = Parse("--interval=" + value);

        var error = Assert.Single(parse.Errors);
        Assert.Equal("--interval must be at least 1 second.", error.Message);
    }

    [Fact]
    public void Interval_not_a_number_is_error()
    {
        var (_, parse) = Parse("--interval", "abc");

        Assert.NotEmpty(parse.Errors);
    }

    [Fact]
    public void Invalid_host_is_error_without_echo()
    {
        var (_, parse) = Parse("--host", "bad host[");

        var error = Assert.Single(parse.Errors);
        Assert.Equal("Invalid --host value.", error.Message);
    }

    [Fact]
    public void Invalid_token_is_error_without_echo()
    {
        var (_, parse) = Parse("--token", "SECRET\nx");

        var error = Assert.Single(parse.Errors);
        Assert.Equal("Invalid --token value.", error.Message);
        Assert.DoesNotContain("SECRET", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--host")]
    [InlineData("--token")]
    [InlineData("--interval")]
    public void Option_without_value_is_error(string option)
    {
        var (_, parse) = Parse(option);

        Assert.NotEmpty(parse.Errors);
    }

    [Fact]
    public void Token_starting_with_dash_is_accepted_in_equals_form()
    {
        var (options, parse) = Parse("--token=-abc");

        Assert.Empty(parse.Errors);
        Assert.Equal("-abc", options.Read(parse).Bar.Token);
    }

    [Fact]
    public void Whitespace_host_and_token_are_accepted_as_unset()
    {
        var (_, parse) = Parse("--host=  ", "--token=  ");

        Assert.Empty(parse.Errors);
    }

    [Theory]
    [InlineData("--host", "--verbose")]
    [InlineData("--token", "--verbose")]
    [InlineData("--token", "--interval")]
    [InlineData("--host", "--help")]
    public void Option_followed_by_another_option_is_error_not_a_value(string option, string next)
    {
        var (_, parse) = Parse(option, next);

        Assert.NotEmpty(parse.Errors);
    }
}
