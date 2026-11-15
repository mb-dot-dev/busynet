using Busynet.Claude.Credentials;
using Busynet.Claude.Usage;
using Busynet.Core.Processes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Refit;

namespace Busynet.Claude;

public static class ClaudeServiceCollectionExtensions
{
    internal const string HttpClientName = "claude";

#pragma warning disable S1075 // Fixed, well-known API endpoint.
    private const string AnthropicApiBaseAddress = "https://api.anthropic.com";
#pragma warning restore S1075

    public static IServiceCollection AddClaude(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The Authorization header and URL carry the token: never let IHttpClientFactory log them.
        services.AddHttpClient(HttpClientName, c =>
        {
            c.BaseAddress = new Uri(AnthropicApiBaseAddress);
            c.Timeout = ClaudeUsageOptions.DefaultTimeout;
        }).RemoveAllLoggers();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.TryAddSingleton(ClaudeCredentialOptions.Default);
        services.TryAddSingleton(ClaudeUsageOptions.Default);
        services.TryAddSingleton<IClaudeTokenSource, ClaudeTokenSource>();
        services.TryAddTransient<IClaudeUsageApi>(sp =>
            RestService.ForGenerated<IClaudeUsageApi>(sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName)));
        services.TryAddTransient<IClaudeUsage, ClaudeUsageClient>();
        return services;
    }
}
