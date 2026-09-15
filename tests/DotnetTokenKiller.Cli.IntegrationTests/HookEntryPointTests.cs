using System.Text;
using FluentAssertions;

namespace DotnetTokenKiller.Cli.IntegrationTests;

public sealed class HookEntryPointTests
{
    private const string ClaudePayload = """{"tool_input":{"command":"dotnet build"}}""";

    [Fact]
    public void Run_RewritingPayload_PrintsTheReplyAndANewline()
    {
        var (exitCode, stdout, stderr) = Run(["claude"], ClaudePayload);

        exitCode.Should().Be(0);
        stdout.Should().Contain("dtk dotnet build").And.EndWith("\n");
        stderr.Should().BeEmpty();
    }

    [Fact]
    public void Run_NothingToRewrite_PrintsNothing()
    {
        var (exitCode, stdout, _) = Run(["claude"], """{"tool_input":{"command":"ls"}}""");

        exitCode.Should().Be(0);
        stdout.Should().BeEmpty();
    }

    [Theory]
    [InlineData([new string[0]])]
    [InlineData([new[] { "cursor" }])]
    [InlineData([new[] { "claude", "extra" }])]
    public void Run_BadArguments_PrintsUsageToStderrAndExitsZero(string[] args)
    {
        var (exitCode, stdout, stderr) = Run(args, ClaudePayload);

        exitCode.Should().Be(0, "a harness blocks the tool call on a non-zero hook exit");
        stdout.Should().BeEmpty();
        stderr.Should().Contain("dtk hook <claude|gemini|copilot-cli|codex|opencode>");
    }

    [Fact]
    public void Run_StdinIsATerminal_DoesNotWaitForInput()
    {
        var opened = false;
        using var output = new MemoryStream();
        using var error = new StringWriter();

        var exitCode = HookEntryPoint.Run(["claude"], isInputRedirected: false,
            () => { opened = true; return new MemoryStream(); }, () => output, error);

        exitCode.Should().Be(0);
        opened.Should().BeFalse("a person who types 'dtk hook claude' must not be left waiting on stdin");
        error.ToString().Should().Contain("dtk hook <claude|gemini|copilot-cli|codex|opencode>");
    }

    [Fact]
    public void Run_InputStreamThrows_ExitsZeroWithNoOutput()
    {
        using var output = new MemoryStream();
        using var error = new StringWriter();

        var exitCode = HookEntryPoint.Run(["gemini"], isInputRedirected: true,
            () => throw new IOException("broken pipe"), () => output, error);

        exitCode.Should().Be(0);
        output.Length.Should().Be(0);
    }

    private static (int ExitCode, string StdOut, string StdErr) Run(string[] args, string stdin)
    {
        using var output = new MemoryStream();
        using var error = new StringWriter();
        var exitCode = HookEntryPoint.Run(args, isInputRedirected: true,
            () => new MemoryStream(Encoding.UTF8.GetBytes(stdin)), () => output, error);
        return (exitCode, Encoding.UTF8.GetString(output.ToArray()), error.ToString());
    }
}
