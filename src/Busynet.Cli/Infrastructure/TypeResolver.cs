using Spectre.Console.Cli;

namespace Busynet.Cli.Infrastructure;

/// <summary>Resolves from the built provider; Spectre disposes it (and so flushes the loggers) after the run.</summary>
public sealed class TypeResolver(IServiceProvider provider) : ITypeResolver, IDisposable
{
    public object? Resolve(Type? type) => type is null ? null : provider.GetService(type);

    public void Dispose() => (provider as IDisposable)?.Dispose();
}
