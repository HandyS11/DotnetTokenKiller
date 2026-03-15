using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

[Trait("Category", "Integration")]
public class DotnetFormatIntegrationTests
{
    private static readonly string SampleApp =
        IntegrationTestHelper.SamplePath("SampleApp");

    private const string EditorConfigContent = "root = true\n\n[*.cs]\ntrim_trailing_whitespace = true\n";
    private const string EditorConfigFile = ".editorconfig";
    private const string DirectoryBuildPropsContent =
        "<Project><PropertyGroup>" +
        "<TargetFramework>net10.0</TargetFramework>" +
        "<LangVersion>14</LangVersion>" +
        "<ImplicitUsings>enable</ImplicitUsings>" +
        "<Nullable>enable</Nullable>" +
        "</PropertyGroup></Project>";
    private const string DirectoryBuildPropsFile = "Directory.Build.props";
    private const string ProgramCsFile = "Program.cs";
    private const string SampleAppCsproj = "SampleApp.csproj";

    [Fact]
    public async Task Format_SampleApp_VerifyNoChanges_OutputIsNoChanges()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "format", SampleApp, "--", "--verify-no-changes", "--verbosity", "normal");

        exitCode.Should().Be(0);
        output.Trim().Should().StartWith("✓ dotnet format");
        output.Should().Contain("no changes");
    }

    [Fact]
    public async Task Format_TempCopy_WithTrailingWhitespace_OutputStartsWithFilesNeedFormatting()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            CopyDirectory(SampleApp, tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, DirectoryBuildPropsFile), DirectoryBuildPropsContent);
            await File.WriteAllTextAsync(Path.Combine(tempDir, EditorConfigFile), EditorConfigContent);
            await File.AppendAllTextAsync(Path.Combine(tempDir, ProgramCsFile), "   ");

            var tempCsproj = Path.Combine(tempDir, SampleAppCsproj);
            var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
                "dotnet", "format", tempCsproj, "--", "--verify-no-changes");

            exitCode.Should().NotBe(0);
            output.Trim().Should().StartWith("dotnet format: 1 file need formatting");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Format_TempCopy_WithTrailingWhitespace_FilenameInOutput()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            CopyDirectory(SampleApp, tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, DirectoryBuildPropsFile), DirectoryBuildPropsContent);
            await File.WriteAllTextAsync(Path.Combine(tempDir, EditorConfigFile), EditorConfigContent);
            await File.AppendAllTextAsync(Path.Combine(tempDir, ProgramCsFile), "   ");

            var tempCsproj = Path.Combine(tempDir, SampleAppCsproj);
            var (output, _) = await IntegrationTestHelper.RunDtkAsync(
                "dotnet", "format", tempCsproj, "--", "--verify-no-changes");

            output.Should().Contain(ProgramCsFile);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Format_TempCopy_TempDirDeletedInTeardown()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            CopyDirectory(SampleApp, tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, DirectoryBuildPropsFile), DirectoryBuildPropsContent);
            var tempCsproj = Path.Combine(tempDir, SampleAppCsproj);
            await IntegrationTestHelper.RunDtkAsync(
                "dotnet", "format", tempCsproj, "--", "--verify-no-changes");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }

        Directory.Exists(tempDir).Should().BeFalse("temp directory should be deleted in teardown");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        foreach (var dir in Directory.GetDirectories(source))
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
    }
}
