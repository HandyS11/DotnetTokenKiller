using DotnetTokenKiller.Domain;
using DotnetTokenKiller.Domain.Filters;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotnetTokenKiller.Application.Tests;

/// <summary>
/// Binds the Application layer's restatements of the supported subcommands back to
/// <see cref="DotnetSubcommands"/>. These are the seams generation cannot close: a registration
/// that is simply absent produces no compile error and no runtime error until a user hits it.
/// </summary>
public sealed class SubcommandBindingTests
{
    [Fact]
    public void EverySubcommand_ResolvesAKeyedOutputFilter()
    {
        var provider = new ServiceCollection().AddApplication().BuildServiceProvider();

        foreach (var subcommand in DotnetSubcommands.Ordered)
        {
            var filter = provider.GetKeyedService<IOutputFilter>(subcommand);

            filter.Should().NotBeNull(
                "'{0}' is a canonical subcommand, so AddApplication must register a filter keyed to it",
                subcommand);
        }
    }

    [Fact]
    public void NoFilter_IsRegisteredUnderANonCanonicalKey()
    {
        var services = new ServiceCollection().AddApplication();

        var keys = services
            .Where(descriptor => descriptor.ServiceType == typeof(IOutputFilter))
            .Select(descriptor => descriptor.ServiceKey)
            .OfType<string>();

        keys.Should().BeEquivalentTo(DotnetSubcommands.Ordered);
    }
}
