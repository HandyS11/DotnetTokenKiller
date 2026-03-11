using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class DotnetEfFilterTests
{
    private readonly DotnetEfFilter _sut = new();

    [Fact]
    public Task Apply_MigrationsListFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_ef_raw.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public void Apply_MigrationsListFixture_SavingsAtLeast70Percent()
    {
        var fixture = LoadFixture("dotnet_ef_raw.txt");
        var result = _sut.Apply(fixture);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(70.0, "ef filter should achieve ≥70% savings");
    }

    [Theory]
    [InlineData("Build started")]
    [InlineData("Build succeeded")]
    [InlineData("Entity Framework Core")]
    [InlineData("Finding DbContext")]
    [InlineData("Using context")]
    public void Apply_MigrationsListFixture_DoesNotContainNoiseLine(string noiseLine)
    {
        var fixture = LoadFixture("dotnet_ef_raw.txt");
        _sut.Apply(fixture).Should().NotContain(noiseLine);
    }

    [Fact]
    public void Apply_MigrationsListFixture_ContainsMigrationCount()
    {
        var fixture = LoadFixture("dotnet_ef_raw.txt");
        var result = _sut.Apply(fixture);
        result.Should().Contain("3 migrations");
        result.Should().Contain("latest:");
    }

    [Fact]
    public void Apply_DatabaseUpdate_ReturnsUpdatedSummary()
    {
        const string input = """
                             Build started...
                             Build succeeded.
                             Entity Framework Core .NET Command-line Tools 8.0.0
                             Finding DbContext classes...
                             Using context 'AppDbContext'.
                             Applying migration '20231101000000_AddProductsTable'.
                             Done.
                             """;
        _sut.Apply(input).Should().Be("✓ database updated (1 migration applied)\n");
    }

    [Fact]
    public void Apply_DatabaseUpdateMultipleMigrations_ReturnsUpdatedSummaryPlural()
    {
        const string input = """
                             Build started...
                             Build succeeded.
                             Entity Framework Core .NET Command-line Tools 8.0.0
                             Finding DbContext classes...
                             Using context 'AppDbContext'.
                             Applying migration '20231101000000_AddProductsTable'.
                             Applying migration '20231201000000_AddOrdersTable'.
                             Done.
                             """;
        _sut.Apply(input).Should().Be("✓ database updated (2 migrations applied)\n");
    }

    [Fact]
    public void Apply_DatabaseUpdateAlreadyUpToDate_ReturnsFallback()
    {
        const string input = """
                             Build started...
                             Build succeeded.
                             Entity Framework Core .NET Command-line Tools 8.0.0
                             Finding DbContext classes...
                             Using context 'AppDbContext'.
                             Done.
                             """;
        _sut.Apply(input).Should().Be("✓ database updated (already up-to-date)\n");
    }

    [Fact]
    public void Apply_MigrationAdd_ReturnsMigrationAdded()
    {
        const string input = """
                             Build started...
                             Build succeeded.
                             Entity Framework Core .NET Command-line Tools 8.0.0
                             Finding DbContext classes...
                             Using context 'AppDbContext'.
                             To undo this action, run 'dotnet ef migrations remove'
                             """;
        _sut.Apply(input).Should().Be("✓ migration added\n");
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
        var assembly = typeof(DotnetEfFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
