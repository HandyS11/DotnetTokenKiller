using System.Reflection;
using DotnetTokenKiller.Domain.Filters;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Domain.Tests;

/// <summary>
/// Binds every Domain-level restatement of the supported dotnet subcommands back to
/// <see cref="DotnetSubcommands"/>. Without this, a new filter key can be added without a
/// canonical entry and nothing fails until adoption is silently zero.
/// </summary>
public class DotnetSubcommandsTests
{
    [Fact]
    public void Ordered_IsTheDisplayOrderUsedByTheCli()
    {
        DotnetSubcommands.Ordered.Should().Equal("build", "test", "restore", "clean", "format");
    }

    [Fact]
    public void Sorted_IsOrdinalAlphabetical_WithTheSameMembersAsOrdered()
    {
        DotnetSubcommands.Sorted.Should().BeInAscendingOrder(StringComparer.Ordinal);
        DotnetSubcommands.Sorted.Should().BeEquivalentTo(DotnetSubcommands.Ordered);
    }

    [Fact]
    public void All_ContainsEveryOrderedEntry_CaseInsensitively()
    {
        DotnetSubcommands.All.Should().BeEquivalentTo(DotnetSubcommands.Ordered);
        DotnetSubcommands.All.Contains("BUILD").Should().BeTrue();
    }

    [Fact]
    public void FilterKeys_ExposesExactlyTheCanonicalSubcommands()
    {
        var keys = typeof(FilterKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);

        keys.Should().BeEquivalentTo(
            DotnetSubcommands.Ordered,
            "every keyed IOutputFilter must correspond to a canonical subcommand and vice versa");
    }
}
