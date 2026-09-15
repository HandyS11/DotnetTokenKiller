using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class SharedInstructionArtifactsTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-shared-{Guid.NewGuid()}");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task Section_IsExactlyWhatGeminiWrites()
    {
        // Antigravity CLI reads the ~/.gemini/GEMINI.md that `dtk init gemini --global` writes; one constant keeps
        // the two providers from rewriting each other's section.
        await new GeminiCliIntegrator(new HomePaths(Path.Combine(_tempDir, "home"))).IntegrateAsync(_tempDir, false, default);

        (await File.ReadAllTextAsync(Path.Combine(_tempDir, "GEMINI.md"))).Should().Be(SharedInstructionArtifacts.Section);
    }

    [Fact]
    public void SkillMarkdown_SatisfiesEveryHarnessSkillLoader()
    {
        var frontmatter = SharedInstructionArtifacts.SkillMarkdown.Split("---")[1];

        // OpenCode requires a lowercase-hyphenated name equal to the folder name, at most 64 characters for Codex.
        // Codex, OpenCode and Antigravity all require a description.
        var folder = Path.GetFileName(Path.GetDirectoryName(SharedInstructionArtifacts.SkillPath("root")));
        folder.Should().MatchRegex("^[a-z0-9]+(-[a-z0-9]+)*$");
        folder.Length.Should().BeLessThanOrEqualTo(64);
        frontmatter.Should().Contain($"name: {folder}\n");
        frontmatter.Should().Contain("description: '");
    }

    [Fact]
    public async Task WriteAgentsFilesAsync_FreshThenRepeated_CreatesBothThenReportsBothUnchanged()
    {
        var agents = Path.Combine(_tempDir, "AGENTS.md");
        var skills = Path.Combine(_tempDir, ".agents", "skills");

        var first = new IntegrationContext(false);
        await SharedInstructionArtifacts.WriteAgentsFilesAsync(agents, skills, first, default);
        var second = new IntegrationContext(false);
        await SharedInstructionArtifacts.WriteAgentsFilesAsync(agents, skills, second, default);

        first.Created.Should().Equal(agents, SharedInstructionArtifacts.SkillPath(skills));
        second.Unchanged.Should().Equal(agents, SharedInstructionArtifacts.SkillPath(skills));
        (await File.ReadAllTextAsync(agents)).Should().Be(SharedInstructionArtifacts.Section);
        ArtifactStamping.IsAuthentic(await File.ReadAllTextAsync(SharedInstructionArtifacts.SkillPath(skills))).Should().BeTrue();
    }
}
