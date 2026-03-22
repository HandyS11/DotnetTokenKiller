using DotnetTokenKiller.Cli;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

public sealed class ArgumentPreprocessorTests
{
    // ── IsPassthrough ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("dotnet", "run")]
    [InlineData("dotnet", "publish")]
    [InlineData("dotnet", "pack")]
    [InlineData("DOTNET", "RUN")]
    public void IsPassthrough_ReturnsTrue_ForUnknownSubcommand(string exe, string sub)
    {
        ArgumentPreprocessor.IsPassthrough([exe, sub]).Should().BeTrue();
    }

    [Theory]
    [InlineData("dotnet", "build")]
    [InlineData("dotnet", "test")]
    [InlineData("dotnet", "restore")]
    [InlineData("dotnet", "clean")]
    [InlineData("DOTNET", "BUILD")]
    public void IsPassthrough_ReturnsFalse_ForKnownSubcommand(string exe, string sub)
    {
        ArgumentPreprocessor.IsPassthrough([exe, sub]).Should().BeFalse();
    }

    [Fact]
    public void IsPassthrough_ReturnsFalse_WhenFewerThanTwoArgs()
    {
        ArgumentPreprocessor.IsPassthrough(["dotnet"]).Should().BeFalse();
        ArgumentPreprocessor.IsPassthrough([]).Should().BeFalse();
    }

    [Fact]
    public void IsPassthrough_ReturnsFalse_WhenFirstArgIsNotDotnet()
    {
        ArgumentPreprocessor.IsPassthrough(["other", "run"]).Should().BeFalse();
    }

    // ── InsertSeparator ──────────────────────────────────────────────────────

    [Fact]
    public void InsertSeparator_InsertsDoubleDash_BeforeDotnetArgs()
    {
        var result = ArgumentPreprocessor.InsertSeparator(
            ["dotnet", "build", "MyProject.slnx"]);

        result.Should().Equal("dotnet", "build", "--", "MyProject.slnx");
    }

    [Fact]
    public void InsertSeparator_PartitionsDtkFlagsBeforeDoubleDash()
    {
        var result = ArgumentPreprocessor.InsertSeparator(
            ["dotnet", "test", "-v", "--filter", "Category=Unit"]);

        result.Should().Equal("dotnet", "test", "-v", "--", "--filter", "Category=Unit");
    }

    [Fact]
    public void InsertSeparator_PartitionsMultipleDtkFlagsBeforeDoubleDash()
    {
        var result = ArgumentPreprocessor.InsertSeparator(
            ["dotnet", "build", "--show-log", "-v", "MyProject.slnx"]);

        result.Should().Equal("dotnet", "build", "--show-log", "-v", "--", "MyProject.slnx");
    }

    [Fact]
    public void InsertSeparator_ReturnsOriginal_WhenDoubleDashAlreadyPresent()
    {
        var args = new[] { "dotnet", "build", "--", "MyProject.slnx" };

        var result = ArgumentPreprocessor.InsertSeparator(args);

        result.Should().BeSameAs(args);
    }

    [Fact]
    public void InsertSeparator_ReturnsOriginal_WhenOnlyDtkFlagsPresent()
    {
        var args = new[] { "dotnet", "build", "-v" };

        var result = ArgumentPreprocessor.InsertSeparator(args);

        result.Should().BeSameAs(args);
    }

    [Fact]
    public void InsertSeparator_ReturnsOriginal_WhenExactlyTwoArgs()
    {
        var args = new[] { "dotnet", "build" };

        var result = ArgumentPreprocessor.InsertSeparator(args);

        result.Should().BeSameAs(args);
    }

    [Fact]
    public void InsertSeparator_ReturnsOriginal_WhenUnknownSubcommand()
    {
        var args = new[] { "dotnet", "publish", "MyProject.csproj" };

        var result = ArgumentPreprocessor.InsertSeparator(args);

        result.Should().BeSameAs(args);
    }

    [Fact]
    public void InsertSeparator_IsCaseInsensitive_ForSubcommand()
    {
        var result = ArgumentPreprocessor.InsertSeparator(
            ["DOTNET", "BUILD", "MyProject.slnx"]);

        result.Should().Equal("DOTNET", "BUILD", "--", "MyProject.slnx");
    }
}
