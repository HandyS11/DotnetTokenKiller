using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class DotnetTestFilterTests
{
    private readonly DotnetTestFilter _sut = new("/home/handys11/Dev/DotnetTokenKiller");

    [Fact]
    public Task Apply_AllPassFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_test_all_pass.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public Task Apply_FailuresFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_test_failures.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public void Apply_AllPassFixture_SavingsAtLeast90Percent()
    {
        var fixture = LoadFixture("dotnet_test_all_pass.txt");
        var result = _sut.Apply(fixture);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(90.0, "test all-pass filter should achieve ≥90% savings");
    }

    [Fact]
    public void Apply_FailuresFixture_SavingsAtLeast70Percent()
    {
        var fixture = LoadFixture("dotnet_test_failures.txt");
        var result = _sut.Apply(fixture);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(70.0, "test failures filter should achieve ≥70% savings");
    }

    [Theory]
    [InlineData("MSBuild version 17.11")]
    [InlineData("Microsoft (R) Test Execution")]
    [InlineData("Copyright (c) Microsoft")]
    [InlineData("Starting test execution, please wait...")]
    [InlineData("A total of 1 test files matched")]
    [InlineData("Determining projects to restore")]
    public void Apply_AllPassFixture_DoesNotContainNoiseLine(string noiseLine)
    {
        var fixture = LoadFixture("dotnet_test_all_pass.txt");
        _sut.Apply(fixture).Should().NotContain(noiseLine);
    }

    [Fact]
    public Task Apply_ZeroTestsFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_test_zero.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public void Apply_ZeroTestsFixture_ReturnsZeroTestsMessage()
    {
        var fixture = LoadFixture("dotnet_test_zero.txt");
        _sut.Apply(fixture).Should().Be("✓ dotnet test: 0 tests found\n");
    }

    [Fact]
    public void Apply_NullInput_ReturnsNonNull()
    {
        _sut.Apply(null!).Should().NotBeNull();
    }

    [Fact]
    public void Apply_EmptyInput_ReturnsNonNull()
    {
        _sut.Apply(string.Empty).Should().NotBeNull();
    }

    private static string LoadFixture(string resourceName)
    {
        var assembly = typeof(DotnetTestFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
