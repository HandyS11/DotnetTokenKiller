using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Domain.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

public class DotnetEfFilterTests
{
    private readonly DotnetEfFilter _sut = new("/test/project/root");

    [Fact]
    public void Apply_MigrationsAdd_KeepsResultThenStatus()
    {
        var result = Run("dotnet_ef_migrations_add.txt", 0, "migrations", "add", "Initial");

        result.Should().Be(
            "Done. To undo this action, use 'ef migrations remove'\n" +
            "✓ dotnet ef migrations add: done\n");
    }

    [Fact]
    public void Apply_MigrationsList_CountsMigrationsKeepingPending()
    {
        var result = Run("dotnet_ef_migrations_list.txt", 0, "migrations", "list");

        result.Should().Be(
            "20261104221252_Initial (Pending)\n" +
            "✓ dotnet ef migrations list: 1 migration\n");
    }

    [Fact]
    public void Apply_MigrationsList_PluralAndZero()
    {
        const string two = "Build started...\nBuild succeeded.\n20260101000000_A\n20260102000000_B (Pending)\n";

        _sut.Apply(two, 0, Context("migrations", "list")).Output.Should().EndWith(
            "✓ dotnet ef migrations list: 2 migrations\n");
        _sut.Apply("Build started...\nBuild succeeded.\n", 0, Context("migrations", "list")).Output.Should().Be(
            "✓ dotnet ef migrations list: 0 migrations\n");
    }

    [Fact]
    public void Apply_MigrationsRemove_KeepsResultLines()
    {
        var result = Run("dotnet_ef_migrations_remove.txt", 0, "migrations", "remove");

        result.Should().Be(
            "Removing migration '20261104221305_AddRating'.\n" +
            "Reverting the model snapshot.\n" +
            "Done.\n" +
            "✓ dotnet ef migrations remove: done\n");
    }

    [Fact]
    public void Apply_DatabaseUpdate_DropsLockNoise()
    {
        var result = Run("dotnet_ef_database_update.txt", 0, "database", "update");

        result.Should().Be(
            "Applying migration '20261104221252_Initial'.\n" +
            "Done.\n" +
            "✓ dotnet ef database update: done\n");
    }

    [Fact]
    public void Apply_Logging_CollapsesInfoBlocks_KeepsWarn()
    {
        var result = Run("dotnet_ef_database_update_logging.txt", 0, "database", "update");

        result.Should().Be(
            "(12 log messages hidden — dtk log)\n" +
            "Applying migration '20261104221252_Initial'.\n" +
            "Done.\n" +
            "✓ dotnet ef database update: done\n");

        const string warn = "Build started...\nBuild succeeded.\n" +
                            "info: App.Category[1]\n      Starting\n" +
                            "warn: App.Category[2]\n      Careful\n      second line\n" +
                            "dbug: App.Category[3]\n      Detail\n" +
                            "Done.\n";
        _sut.Apply(warn, 0, Context("database", "update")).Output.Should().Be(
            "(2 log messages hidden — dtk log)\n" +
            "warn: App.Category[2]\n      Careful\n      second line\n" +
            "Done.\n" +
            "✓ dotnet ef database update: done\n");
    }

    [Fact]
    public void Apply_Verbose_KeepsDiagnostics()
    {
        var result = Run("dotnet_ef_migrations_add_verbose.txt", 0, "migrations", "add", "AddRating", "--verbose");

        result.Should().Contain("Using project ");
        result.Should().Contain("Finding DbContext classes...");
        result.Should().Contain("dotnet exec ");
        result.Should().NotContain("Build started...");
        result.Should().NotContain("Build succeeded.\n");
        result.Should().EndWith("Done. To undo this action, use 'ef migrations remove'\n✓ dotnet ef migrations add: done\n");
    }

    [Fact]
    public void Apply_Verbose_KeepsLogBlocks()
    {
        const string input = "Build started...\nBuild succeeded.\ninfo: App.Category[1]\n      Starting\nDone.\n";

        _sut.Apply(input, 0, Context("database", "update", "-v")).Output.Should().Be(
            "info: App.Category[1]\n      Starting\nDone.\n✓ dotnet ef database update: done\n");
    }

    [Fact]
    public void Apply_BuildFailed_OmitsProjectWhenNotGiven()
    {
        var result = Run("dotnet_ef_migrations_add_build_failed.txt", 1, "migrations", "add", "Oops");

        result.Should().Be(
            "✗ dotnet ef migrations add failed (exit 1): build failed — run dtk dotnet build to see the errors\n");
    }

    [Theory]
    [InlineData("-p", "src/Data")]
    [InlineData("--project", "src/Data")]
    public void Apply_BuildFailed_NamesProjectFromArguments(string option, string value)
    {
        var result = Run("dotnet_ef_migrations_add_build_failed.txt", 1, "migrations", "add", "X", option, value);

        result.Should().Be(
            "✗ dotnet ef migrations add failed (exit 1): build failed — run dtk dotnet build src/Data to see the errors\n");
    }

    [Fact]
    public void Apply_BuildFailed_NamesProjectFromEqualsForm()
    {
        var result = Run("dotnet_ef_migrations_add_build_failed.txt", 1, "add", "X", "--project=src/Data");

        result.Should().EndWith("run dtk dotnet build src/Data to see the errors\n");
    }

    [Fact]
    public void Apply_RuntimeError_KeepsFailBlockAndMessages_ThenStatus()
    {
        var result = Run("dotnet_ef_database_update_error.txt", 1, "database", "update");

        result.Should().StartWith("(2 log messages hidden — dtk log)\n");
        result.Should().Contain("fail: Microsoft.EntityFrameworkCore.Database.Command[20102]\n");
        result.Should().Contain("SQLite Error 8: 'attempt to write a readonly database'.\n");
        result.Should().Contain("   at Microsoft.Data.Sqlite.SqliteException.ThrowExceptionForRC");
        result.Should().NotContain("Build started...");
        result.Should().NotContain("Acquiring an exclusive lock");
        result.Should().EndWith("✗ dotnet ef database update failed (exit 1)\n");
    }

    [Fact]
    public void Apply_ArgumentsWithoutGroup_InfersGroupFromVerb()
    {
        var context = new FilterContext { Arguments = ["update", "--no-build"] };

        _sut.Apply("Build started...\nBuild succeeded.\nDone.\n", 0, context).Output.Should().EndWith(
            "✓ dotnet ef database update: done\n");
    }

    [Fact]
    public void Apply_UnknownContext_UsesBareStatus()
    {
        _sut.Apply("Done.\n", 0).Should().Be("Done.\n✓ dotnet ef: done\n");
        _sut.Apply("boom\n", 2).Should().Be("boom\n✗ dotnet ef failed (exit 2)\n");
    }

    [Theory]
    [InlineData("dotnet_ef_migrations_add.txt", 0)]
    [InlineData("dotnet_ef_migrations_list.txt", 0)]
    [InlineData("dotnet_ef_migrations_remove.txt", 0)]
    [InlineData("dotnet_ef_database_update.txt", 0)]
    [InlineData("dotnet_ef_database_update_logging.txt", 0)]
    [InlineData("dotnet_ef_migrations_add_build_failed.txt", 1)]
    [InlineData("dotnet_ef_database_update_error.txt", 1)]
    public void Apply_Crlf_MatchesLf(string name, int exitCode)
    {
        var fixture = LoadFixture(name);
        var context = Context("database", "update");

        var result = _sut.Apply(fixture.ReplaceLineEndings("\r\n"), exitCode, context).Output;

        result.Should().NotContain("\r");
        result.Should().Be(_sut.Apply(fixture, exitCode, context).Output);
    }

    private string Run(string fixture, int exitCode, params string[] arguments) =>
        _sut.Apply(LoadFixture(fixture), exitCode, Context(arguments)).Output;

    private static FilterContext Context(params string[] arguments) => new() { Arguments = arguments };

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
