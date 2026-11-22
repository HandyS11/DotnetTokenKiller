using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Application.Integration.Hooks;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration.Hooks;

public sealed class DotnetProjectDetectorTests : IDisposable
{
    private readonly string _root = Path.GetFullPath(
        Path.Combine(Path.GetTempPath(), "dtk-detect-" + Guid.NewGuid().ToString("N")));

    public DotnetProjectDetectorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        foreach (var link in Directory.EnumerateDirectories(_root, "*", SearchOption.AllDirectories)
                     .Where(directory => new DirectoryInfo(directory).LinkTarget is not null).ToList())
        {
            Directory.Delete(link);
        }

        Directory.Delete(_root, true);
    }

    [Theory]
    [InlineData("App.csproj")]
    [InlineData("App.fsproj")]
    [InlineData("App.vbproj")]
    [InlineData("App.sln")]
    [InlineData("App.slnx")]
    [InlineData("App.slnf")]
    [InlineData("global.json")]
    [InlineData("Directory.Build.props")]
    public void IsDotnetProject_MarkerInTheDirectory_IsTrue(string marker)
    {
        Write(marker);

        DotnetProjectDetector.IsDotnetProject(_root).Should().BeTrue();
    }

    [Fact]
    public void IsDotnetProject_MarkerInAnAncestor_IsTrue()
    {
        Write("App.slnx");
        var deep = Directory.CreateDirectory(Path.Combine(_root, "src", "App", "Sub")).FullName;

        DotnetProjectDetector.IsDotnetProject(deep).Should().BeTrue();
    }

    [Fact]
    public void IsDotnetProject_ProjectTwoLevelsDown_IsTrue()
    {
        Write("README.md");
        Write(Path.Combine("backend", "Api", "Api.csproj"));

        DotnetProjectDetector.IsDotnetProject(_root).Should().BeTrue();
    }

    [Fact]
    public void IsDotnetProject_ProjectThreeLevelsDown_IsFalse()
    {
        Write(Path.Combine("a", "b", "c", "X.csproj"));

        DotnetProjectDetector.IsDotnetProject(_root).Should().BeFalse();
    }

    [Theory]
    [InlineData(".hidden")]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData("node_modules")]
    [InlineData("artifacts")]
    public void IsDotnetProject_ProjectOnlyInASkippedFolder_IsFalse(string folder)
    {
        Write(Path.Combine(folder, "X.csproj"));
        Write(Path.Combine("src", folder, "Y.csproj"));

        DotnetProjectDetector.IsDotnetProject(_root).Should().BeFalse();
    }

    [Fact]
    public void IsDotnetProject_GlobalJsonBelow_IsFalse()
    {
        // Below the directory, only a solution or project file says .NET; a stray global.json or props file does not.
        Write(Path.Combine("tools", "global.json"));
        Write(Path.Combine("tools", "Directory.Build.props"));

        DotnetProjectDetector.IsDotnetProject(_root).Should().BeFalse();
    }

    [Fact]
    public void IsDotnetProject_NoMarker_IsFalse()
    {
        Write("README.md");
        Write("package.json");
        Write(Path.Combine("src", "index.ts"));

        DotnetProjectDetector.IsDotnetProject(_root).Should().BeFalse();
    }

    [Fact]
    public void IsDotnetProject_BudgetSpentBeforeAMatch_IsFalse()
    {
        for (var i = 0; i < 30; i++)
        {
            Directory.CreateDirectory(Path.Combine(_root, $"d{i:D2}"));
        }

        Write(Path.Combine("z", "X.csproj"));

        DotnetProjectDetector.IsDotnetProject(_root, entryBudget: 10).Should().BeFalse();
        DotnetProjectDetector.IsDotnetProject(_root, DotnetProjectDetector.DefaultEntryBudget).Should().BeTrue();
    }

    [Fact]
    public void IsDotnetProject_DoesNotFollowDirectorySymlinks()
    {
        // A link may lead anywhere, a huge tree included: a project reached only through one does not count.
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var elsewhere = Path.Combine(_root, "elsewhere");
        Write(Path.Combine("elsewhere", "Api", "Api.csproj"));
        var work = Directory.CreateDirectory(Path.Combine(_root, "work")).FullName;
        Directory.CreateSymbolicLink(Path.Combine(work, "linked"), elsewhere);

        DotnetProjectDetector.IsDotnetProject(work).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative/path")]
    public void IsDotnetProject_UnusableDirectory_IsFalse(string? directory)
    {
        DotnetProjectDetector.IsDotnetProject(directory).Should().BeFalse();
    }

    [Fact]
    public void IsDotnetProject_MissingOrFileDirectory_IsFalse()
    {
        Write("App.csproj");

        DotnetProjectDetector.IsDotnetProject(Path.Combine(_root, "missing")).Should().BeFalse();
        DotnetProjectDetector.IsDotnetProject(Path.Combine(_root, "App.csproj")).Should().BeFalse();
    }

    [Fact]
    public void AgentRules_AreFourLines()
    {
        IntegrationInstructions.AgentRules.Split('\n').Should().HaveCount(4);
        IntegrationInstructions.AgentRules.Should().EndWith(IntegrationInstructions.AgentRuleBullets);
        IntegrationInstructions.AgentRuleBullets.Split('\n').Should().OnlyContain(line => line.StartsWith("- "));
    }

    private void Write(string relative)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
    }
}
