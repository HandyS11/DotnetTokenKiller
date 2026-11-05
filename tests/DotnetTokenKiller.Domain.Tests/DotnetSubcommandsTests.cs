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
        DotnetSubcommands.Ordered.Should().Equal(
            "build", "test", "restore", "clean", "format", "list package", "publish", "pack",
            "tool restore", "ef migrations", "ef database", "stryker", "jb cleanupcode");
    }

    [Fact]
    public void ToolBacked_IsTheSubcommandsWhoseToolOwnsTheShortOptions()
    {
        // `tool restore` is a dotnet verb: dotnet's own option rules apply to it, unlike to these tools.
        DotnetSubcommands.ToolBacked.Should().BeEquivalentTo("ef migrations", "ef database", "stryker", "jb cleanupcode");
        DotnetSubcommands.ToolBacked.Should().BeSubsetOf(DotnetSubcommands.Ordered);
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

    [Fact]
    public void TryMatch_SingleTokenSubcommand_ReturnsNameAndOneToken()
    {
        DotnetSubcommands.TryMatch(["build", "MyApp.slnx"], out var match).Should().BeTrue();

        match.Name.Should().Be("build");
        match.TokenCount.Should().Be(1);
    }

    [Fact]
    public void TryMatch_IsCaseInsensitive_ButReturnsCanonicalCasing()
    {
        DotnetSubcommands.TryMatch(["BUILD"], out var match).Should().BeTrue();

        match.Name.Should().Be("build");
    }

    [Theory]
    [InlineData(new[] { "tool", "restore", "--interactive" }, "tool restore", 2)]
    [InlineData(new[] { "ef", "migrations", "add", "Initial" }, "ef migrations", 2)]
    [InlineData(new[] { "EF", "Database", "update" }, "ef database", 2)]
    [InlineData(new[] { "stryker", "--break-at", "80" }, "stryker", 1)]
    [InlineData(new[] { "jb", "cleanupcode", "App.slnx" }, "jb cleanupcode", 2)]
    public void TryMatch_ToolSubcommands_ConsumeTheirTokens(string[] args, string name, int tokenCount)
    {
        DotnetSubcommands.TryMatch(args, out var match).Should().BeTrue();

        match.Should().Be(new SubcommandMatch(name, tokenCount));
    }

    [Theory]
    [InlineData((object)new[] { "tool", "run", "x" })]
    [InlineData((object)new[] { "tool", "install", "dotnet-ef" })]
    [InlineData((object)new[] { "ef", "dbcontext", "list" })]
    [InlineData((object)new[] { "ef" })]
    [InlineData((object)new[] { "jb", "inspectcode", "App.slnx" })]
    public void TryMatch_OtherVerbsOfTheSameTools_ReturnFalse(string[] args)
    {
        DotnetSubcommands.TryMatch(args, out _).Should().BeFalse();
    }

    [Fact]
    public void TryMatch_UnknownSubcommand_ReturnsFalse()
    {
        DotnetSubcommands.TryMatch(["run"], out var match).Should().BeFalse();

        match.Should().Be(default(SubcommandMatch));
    }

    [Fact]
    public void TryMatch_EmptyArgs_ReturnsFalse()
    {
        DotnetSubcommands.TryMatch([], out _).Should().BeFalse();
    }

    [Fact]
    public void TryMatch_MultiTokenSubcommand_ConsumesBothTokens()
    {
        DotnetSubcommands.TryMatch(["list", "package", "--outdated"], ["list package"], out var match)
            .Should().BeTrue();

        match.Name.Should().Be("list package");
        match.TokenCount.Should().Be(2);
    }

    [Fact]
    public void TryMatch_PrefersTheLongestCandidate_RegardlessOfDeclarationOrder()
    {
        // "list" is declared first, but "list package" must win: a shorter candidate that is a
        // prefix of a longer one would otherwise shadow it and silently route to the wrong filter.
        DotnetSubcommands.TryMatch(["list", "package"], ["list", "list package"], out var match)
            .Should().BeTrue();

        match.Name.Should().Be("list package");
        match.TokenCount.Should().Be(2);
    }

    [Fact]
    public void TryMatch_MultiTokenCandidate_DoesNotMatchOnFirstTokenAlone()
    {
        DotnetSubcommands.TryMatch(["list", "reference"], ["list package"], out _).Should().BeFalse();
    }

    [Fact]
    public void TryMatch_MultiTokenCandidate_DoesNotMatchATruncatedArgList()
    {
        DotnetSubcommands.TryMatch(["list"], ["list package"], out _).Should().BeFalse();
    }
}
