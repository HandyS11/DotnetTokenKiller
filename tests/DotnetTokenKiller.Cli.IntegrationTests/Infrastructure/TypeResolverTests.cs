using DotnetTokenKiller.Cli.Commands;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Infrastructure;

public sealed class TypeResolverTests
{
    [Fact]
    public void Resolve_NullType_ReturnsNull()
    {
        // TypeResolver is internal — access via reflection to cover the null-type guard branch.
        var resolverType = typeof(DotnetBuildCommand).Assembly
            .GetType("DotnetTokenKiller.Cli.Infrastructure.TypeResolver")!;

        var provider = new FakeServiceProvider();
        var resolver = Activator.CreateInstance(resolverType, provider)!;
        var resolveMethod = resolverType.GetMethod("Resolve")!;

        var result = resolveMethod.Invoke(resolver, [(Type?)null]);

        result.Should().BeNull();
        (resolver as IDisposable)?.Dispose();
    }

    private sealed class FakeServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}
