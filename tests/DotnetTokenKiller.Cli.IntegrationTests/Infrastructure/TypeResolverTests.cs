using DotnetTokenKiller.Cli.Commands;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Infrastructure;

public sealed class TypeResolverTests
{
    private static readonly Type ResolverType = typeof(DotnetBuildCommand).Assembly
        .GetType("DotnetTokenKiller.Cli.Infrastructure.TypeResolver")!;

    [Fact]
    public void Resolve_NullType_ReturnsNull()
    {
        // TypeResolver is internal — access via reflection to cover the null-type guard branch.
        var provider = new FakeServiceProvider();
        var resolver = Activator.CreateInstance(ResolverType, provider)!;
        var resolveMethod = ResolverType.GetMethod("Resolve")!;

        var result = resolveMethod.Invoke(resolver, [null]);

        result.Should().BeNull();
        (resolver as IDisposable)?.Dispose();
    }

    [Fact]
    public void Resolve_RegisteredType_ReturnsInstance()
    {
        const string expected = "hello";
        var provider = new FakeServiceProvider(typeof(string), expected);
        var resolver = Activator.CreateInstance(ResolverType, provider)!;
        var resolveMethod = ResolverType.GetMethod("Resolve")!;

        var result = resolveMethod.Invoke(resolver, [typeof(string)]);

        result.Should().BeSameAs(expected);
        (resolver as IDisposable)?.Dispose();
    }

    [Fact]
    public void Resolve_UnregisteredType_ReturnsNull()
    {
        var provider = new FakeServiceProvider();
        var resolver = Activator.CreateInstance(ResolverType, provider)!;
        var resolveMethod = ResolverType.GetMethod("Resolve")!;

        var result = resolveMethod.Invoke(resolver, [typeof(int)]);

        result.Should().BeNull();
        (resolver as IDisposable)?.Dispose();
    }

    [Fact]
    public void Dispose_DisposableProvider_DisposesProvider()
    {
        var provider = new DisposableServiceProvider();
        var resolver = Activator.CreateInstance(ResolverType, provider)!;

        (resolver as IDisposable)?.Dispose();

        provider.Disposed.Should().BeTrue();
    }

    private sealed class FakeServiceProvider(Type? registeredType = null, object? instance = null) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return serviceType == registeredType ? instance : null;
        }
    }

    private sealed class DisposableServiceProvider : IServiceProvider, IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
        }

        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}
