using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Infrastructure;

/// <summary>Resolves services from a Microsoft DI <see cref="IServiceProvider"/>.</summary>
/// <param name="provider">The DI service provider.</param>
internal sealed class TypeResolver(IServiceProvider provider) : ITypeResolver, IDisposable
{
    /// <inheritdoc/>
    public object? Resolve(Type? type)
    {
        return type is null ? null : provider.GetService(type);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (provider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
