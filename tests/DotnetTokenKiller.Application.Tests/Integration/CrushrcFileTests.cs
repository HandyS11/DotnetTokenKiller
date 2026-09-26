using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class CrushrcFileTests : IDisposable
{
    private const string Command = "dtk hook crush";
    private const string UserConfig = "provider add deepseek --type openai-compat\nhook add PreToolUse --name fmt --command 'gofmt -l .'\n";

    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-crushrc-{Guid.NewGuid()}");

    private string RcPath => Path.Combine(_tempDir, ".crushrc");

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private void Seed(string content)
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(RcPath, content);
    }

    private static IntegrationContext Install() => new(force: false);

    private IntegrationContext Uninstall() => IntegrationContext.ForUninstall(_tempDir, new Dictionary<string, string>());

    [Fact]
    public void Section_IsExactlyTheThreeLines()
    {
        CrushrcFile.Section(Command).Should().Be(
            "# >>> dtk (DotnetTokenKiller) >>>\n"
            + "hook add PreToolUse --name dtk --matcher '^bash$' --command 'dtk hook crush'\n"
            + "# <<< dtk <<<\n");
    }

    [Fact]
    public async Task WriteAsync_NoFile_CreatesItWithTheSection()
    {
        var context = Install();

        await CrushrcFile.WriteAsync(RcPath, Command, context, default);

        (await File.ReadAllTextAsync(RcPath)).Should().Be(CrushrcFile.Section(Command));
        context.Created.Should().Equal(RcPath);
    }

    [Fact]
    public async Task WriteAsync_UserConfig_AppendsAfterABlankLineAndKeepsEverything()
    {
        Seed(UserConfig);
        var context = Install();

        await CrushrcFile.WriteAsync(RcPath, Command, context, default);

        (await File.ReadAllTextAsync(RcPath)).Should().Be(UserConfig + "\n" + CrushrcFile.Section(Command));
        context.Updated.Should().Equal(RcPath);
    }

    [Fact]
    public async Task WriteAsync_NoTrailingNewline_StillSeparatesTheSection()
    {
        Seed("option debug true");

        await CrushrcFile.WriteAsync(RcPath, Command, Install(), default);

        (await File.ReadAllTextAsync(RcPath)).Should().Be("option debug true\n\n" + CrushrcFile.Section(Command));
    }

    [Fact]
    public async Task WriteAsync_SecondRun_ReportsUnchanged()
    {
        Seed(UserConfig);
        await CrushrcFile.WriteAsync(RcPath, Command, Install(), default);
        var before = await File.ReadAllTextAsync(RcPath);
        var context = Install();

        await CrushrcFile.WriteAsync(RcPath, Command, context, default);

        context.Unchanged.Should().Equal(RcPath);
        (await File.ReadAllTextAsync(RcPath)).Should().Be(before);
    }

    [Fact]
    public async Task WriteAsync_StaleSection_IsReplacedInPlace()
    {
        Seed("a\n# >>> dtk (DotnetTokenKiller) >>>\nhook add PreToolUse --command 'old'\n# <<< dtk <<<\nb\n");

        await CrushrcFile.WriteAsync(RcPath, Command, Install(), default);

        (await File.ReadAllTextAsync(RcPath)).Should().Be("a\n" + CrushrcFile.Section(Command) + "b\n");
    }

    [Fact]
    public async Task WriteAsync_CrlfFile_WritesTheSectionWithCrlf()
    {
        Seed("option debug true\r\n");

        await CrushrcFile.WriteAsync(RcPath, Command, Install(), default);

        (await File.ReadAllTextAsync(RcPath)).Should().Be(
            "option debug true\r\n\r\n" + CrushrcFile.Section(Command).ReplaceLineEndings("\r\n"));
    }

    [Theory]
    [InlineData("# >>> dtk (DotnetTokenKiller) >>>\nhook add PreToolUse --command x\n")]
    [InlineData("hook add PreToolUse --command x\n# <<< dtk <<<\n")]
    [InlineData("# <<< dtk <<<\n# >>> dtk (DotnetTokenKiller) >>>\n")]
    [InlineData("# >>> dtk (DotnetTokenKiller) >>>\n# <<< dtk <<<\n# >>> dtk (DotnetTokenKiller) >>>\n# <<< dtk <<<\n")]
    public async Task WriteAndRemove_DamagedSection_ThrowAndLeaveTheFileAlone(string content)
    {
        Seed(content);

        var write = () => CrushrcFile.WriteAsync(RcPath, Command, Install(), default);
        var remove = () => CrushrcFile.RemoveAsync(RcPath, Uninstall(), default);

        await write.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{RcPath}*");
        await remove.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{RcPath}*");
        (await File.ReadAllTextAsync(RcPath)).Should().Be(content);
    }

    [Theory]
    [InlineData(UserConfig)]
    [InlineData("option debug true")]
    [InlineData("option debug true\r\n")]
    public async Task InstallThenRemove_RestoresTheUserFileByteForByte(string original)
    {
        ArgumentNullException.ThrowIfNull(original);
        Seed(original);
        await CrushrcFile.WriteAsync(RcPath, Command, Install(), default);
        var context = Uninstall();

        await CrushrcFile.RemoveAsync(RcPath, context, default);

        (await File.ReadAllTextAsync(RcPath)).Should().Be(original.EndsWith('\n') ? original : original + "\n");
        context.Updated.Should().Equal(RcPath);
    }

    [Fact]
    public async Task RemoveAsync_OnlyTheSection_DeletesTheFile()
    {
        await CrushrcFile.WriteAsync(RcPath, Command, Install(), default);
        var context = Uninstall();

        await CrushrcFile.RemoveAsync(RcPath, context, default);

        File.Exists(RcPath).Should().BeFalse();
        context.Removed.Should().Equal(RcPath);
    }

    [Fact]
    public async Task RemoveAsync_NoSection_ReportsUnchanged()
    {
        Seed(UserConfig);
        var context = Uninstall();

        await CrushrcFile.RemoveAsync(RcPath, context, default);

        context.Unchanged.Should().Equal(RcPath);
        (await File.ReadAllTextAsync(RcPath)).Should().Be(UserConfig);
    }

    [Fact]
    public async Task RemoveAsync_NoFile_DoesNothing()
    {
        var context = Uninstall();

        await CrushrcFile.RemoveAsync(RcPath, context, default);

        context.Unchanged.Should().BeEmpty();
        context.Removed.Should().BeEmpty();
    }
}
