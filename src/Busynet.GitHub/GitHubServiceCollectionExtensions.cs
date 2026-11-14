using Busynet.Core.Processes;
using Busynet.GitHub.Actions;
using Busynet.GitHub.Credentials;
using Busynet.GitHub.Git;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Refit;

namespace Busynet.GitHub;

public static class GitHubServiceCollectionExtensions
{
    internal const string HttpClientName = "github";

    public static IServiceCollection AddGitHub(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The Authorization header carries the token: never let IHttpClientFactory log it.
        services.AddHttpClient(HttpClientName, c =>
        {
            c.BaseAddress = new Uri("https://api.github.com");
            c.Timeout = GitHubActionsOptions.DefaultTimeout;
        }).RemoveAllLoggers();

        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.TryAddSingleton<Func<string, string?>>((IServiceProvider _) => Environment.GetEnvironmentVariable);
        services.TryAddSingleton(GitHubActionsOptions.Default);
        services.TryAddSingleton<IGitHubTokenSource, GitHubTokenSource>();
        services.TryAddSingleton<IGitCheckout, GitCheckout>();
        services.TryAddTransient<IGitHubApi>(sp =>
            RestService.ForGenerated<IGitHubApi>(sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName)));
        // Singleton: it caches the token for the whole run.
        services.TryAddSingleton<IGitHubActions, GitHubActionsClient>();
        return services;
    }
}
