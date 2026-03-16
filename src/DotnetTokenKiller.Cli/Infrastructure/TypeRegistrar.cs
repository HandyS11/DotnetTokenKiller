using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Infrastructure;

/// <summary>Bridges Spectre.Console.Cli's type registration to Microsoft DI.</summary>
/// <param name="services">The service collection to register types into.</param>
internal sealed class TypeRegistrar(IServiceCollection services) : ITypeRegistrar
{
    /// <inheritdoc/>
    public ITypeResolver Build()
    {
        return new TypeResolver(services.BuildServiceProvider());
    }

    /// <inheritdoc/>
    public void Register(Type service, Type implementation)
    {
        services.AddSingleton(service, implementation);
    }

    /// <inheritdoc/>
    public void RegisterInstance(Type service, object implementation)
    {
        services.AddSingleton(service, implementation);
    }

    /// <inheritdoc/>
    public void RegisterLazy(Type service, Func<object> factory)
    {
        services.AddSingleton(service, _ => factory());
    }
}
