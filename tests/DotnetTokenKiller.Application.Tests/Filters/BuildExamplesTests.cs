using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Domain.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

/// <summary>
/// Binds the published build examples to the filters that produce them. The examples were
/// hand-maintained prose for several releases and drifted far enough that a documented run
/// reported a different error count than the filter ever emits; nothing failed, because nothing
/// checked. This replays each documented raw capture through the real filter.
/// </summary>
public sealed partial class BuildExamplesTests
{
    /// <summary>
    /// The root the documented captures are normalised to, so the examples stay machine-independent
    /// and the relative paths in them are reproducible on any checkout.
    /// </summary>
    private const string DocumentedRoot = "/repo";

    [Fact]
    public void EveryDocumentedExample_MatchesTheFilterOutput()
    {
        var examples = ParseExamples(File.ReadAllText(ExamplesPath()));

        examples.Should().HaveCountGreaterThan(4, "BUILD.md should document every filtered verb");

        foreach (var (command, rawOutput, documented) in examples)
        {
            // MSBuild prints "Build FAILED." exactly when the run failed, so the captured output
            // carries its own exit code and the examples need no separate annotation.
            var exitCode = rawOutput.Contains("Build FAILED.", StringComparison.Ordinal) ? 1 : 0;

            var actual = FilterFor(command).Apply(rawOutput, exitCode).TrimEnd('\n');

            actual.Should().Be(
                documented,
                "the dtk block documented for '{0}' must be what the filter actually produces",
                command);
        }
    }

    private static IOutputFilter FilterFor(string command)
    {
        return command.Contains("dotnet clean", StringComparison.Ordinal)
            ? new DotnetCleanFilter()
            : new DotnetBuildFilter(DocumentedRoot);
    }

    /// <summary>
    /// Pulls every (command, raw, dtk) triple out of the markdown. Blocks are emitted strictly as
    /// a Raw/dtk pair, so an unpaired block means the document was edited by hand into a shape the
    /// examples can no longer be verified from — which fails rather than silently skipping.
    /// </summary>
    /// <param name="markdown">The full contents of the examples document.</param>
    private static List<(string Command, string RawOutput, string Documented)> ParseExamples(string markdown)
    {
        var blocks = BlockPattern().Matches(markdown);
        var examples = new List<(string, string, string)>();

        for (var i = 0; i < blocks.Count; i += 2)
        {
            var raw = blocks[i];
            raw.Groups["kind"].Value.Should().Be("Raw", "example blocks must alternate Raw then dtk");

            var filtered = blocks.Count > i + 1 ? blocks[i + 1] : null;
            filtered.Should().NotBeNull("the Raw block for '{0}' has no dtk block", raw.Groups["cmd"].Value);
            filtered.Groups["kind"].Value.Should().Be("dtk", "example blocks must alternate Raw then dtk");

            // Normalise line endings: the filter always emits '\n', so a CRLF checkout of the
            // markdown would otherwise fail every comparison on Windows and nowhere else.
            examples.Add((
                raw.Groups["cmd"].Value,
                raw.Groups["body"].Value.ReplaceLineEndings("\n"),
                filtered.Groups["body"].Value.ReplaceLineEndings("\n")));
        }

        return examples;
    }

    private static string ExamplesPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DotnetTokenKiller.slnx")))
            {
                return Path.Combine(dir.FullName, "samples", "examples", "BUILD.md");
            }
        }

        throw new InvalidOperationException(
            $"Could not locate DotnetTokenKiller.slnx above {AppContext.BaseDirectory}.");
    }

    // Matches a labelled example block: "**Raw** (`dotnet build ...`)" followed by its ```sh fence.
    [GeneratedRegex(@"\*\*(?<kind>Raw|dtk)\*\* \(`(?<cmd>[^`]+)`\)\r?\n\r?\n```sh\r?\n(?<body>.*?)\r?\n```",
        RegexOptions.Singleline)]
    private static partial Regex BlockPattern();
}
