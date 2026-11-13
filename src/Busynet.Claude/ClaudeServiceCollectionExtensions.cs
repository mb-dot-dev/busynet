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

    public static IServiceCollection AddClaude(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The Authorization header and URL carry the token: never let IHttpClientFactory log them.
        services.AddHttpClient(HttpClientName, c =>
        {
            c.BaseAddress = new Uri("https://api.anthropic.com");
            c.Timeout = TimeSpan.FromSeconds(15);
        }).RemoveAllLoggers();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.TryAddSingleton(ClaudeCredentialOptions.Default);
        services.TryAddSingleton<IClaudeTokenSource, ClaudeTokenSource>();
        services.TryAddTransient<IClaudeUsageApi>(sp =>
            RestService.For<IClaudeUsageApi>(sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName)));
        services.TryAddTransient<IClaudeUsage, ClaudeUsageClient>();
        return services;
    }
}
