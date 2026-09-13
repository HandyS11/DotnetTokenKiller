using System.Diagnostics.CodeAnalysis;
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
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2067:Target parameter argument does not satisfy 'DynamicallyAccessedMembersAttribute' in call to target method",
        Justification = "Spectre.Console.Cli passes the command, settings and built-in command types it registers "
                        + "(including its hidden 'cli version', 'cli explain' and 'cli opencli' commands, whose "
                        + "constructors take its internal services), and ITypeRegistrar carries no trimming "
                        + "annotations. dtk and Spectre.Console.Cli are rooted (TrimmerRootAssembly), so every "
                        + "constructor survives; AotParityTests and SpectreBuiltInCommandTests run these types "
                        + "through the AOT binary. One of the two trim or AOT suppressions at the Spectre.Console.Cli "
                        + "boundary; see the native AOT design spec.")]
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
