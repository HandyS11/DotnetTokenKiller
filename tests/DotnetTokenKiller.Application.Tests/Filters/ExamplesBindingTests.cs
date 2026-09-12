using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Domain.Filters;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Filters;

/// <summary>
/// Binds the published examples to the filters that produce them. The pages were hand-maintained
/// prose for several releases and drifted into shapes no filter can emit — a documented build
/// reporting a different error count than the header below it, a "Top codes" line longer than the
/// five entries the filter takes, a cited source file belonging to a different sample project.
/// Nothing failed, because nothing checked. This replays every documented capture for real.
/// </summary>
public sealed partial class ExamplesBindingTests
{
    /// <summary>
    /// The root the documented captures are normalised to, so the examples stay machine-independent
    /// and the relative paths in them are reproducible on any checkout.
    /// </summary>
    private const string DocumentedRoot = "/repo";

    /// <summary>Every document that publishes a Raw/dtk example pair, relative to the repo root.</summary>
    public static TheoryData<string> ExampleDocuments() => new(
        "samples/examples/BUILD.md",
        "samples/examples/TEST.md",
        "samples/examples/RESTORE.md",
        "samples/examples/FORMAT.md",
        "README.md");

    [Theory]
    [MemberData(nameof(ExampleDocuments))]
    public void EveryDocumentedExample_MatchesTheFilterOutput(string document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var markdown = File.ReadAllText(ExamplesPath(document));
        var examples = ParseExamples(markdown);

        examples.Should().NotBeEmpty("{0} should document at least one example", document);

        // Bind the count to the page itself. Without this a regex that silently stopped matching
        // would leave most of the document unverified while the test still reported green.
        examples.Should().HaveCount(
            RawLabelPattern().Count(markdown),
            "every **Raw** block in {0} must be parsed and verified, not just the ones the regex "
            + "happened to match",
            document);

        foreach (var example in examples)
        {
            var actual = FilterFor(example.Command).Apply(example.RawOutput, example.ExitCode).TrimEnd('\n');

            actual.Should().Be(
                example.Documented,
                "the dtk block documented in {0} for '{1}' must be what the filter actually produces",
                document,
                example.Command);
        }
    }

    /// <summary>Resolves the filter the documented command would have been dispatched to.</summary>
    /// <param name="command">The documented command line, e.g. "dotnet build foo.csproj".</param>
    /// <exception cref="InvalidOperationException">The documented verb has no filter.</exception>
    private static IOutputFilter FilterFor(string command)
    {
        var verb = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1);

        return verb switch
        {
            "build" => new DotnetBuildFilter(DocumentedRoot),
            "clean" => new DotnetCleanFilter(DocumentedRoot),
            "restore" => new DotnetRestoreFilter(DocumentedRoot),
            "format" => new DotnetFormatFilter(DocumentedRoot),
            "test" => new DotnetTestFilter(DocumentedRoot),
            _ => throw new InvalidOperationException($"No filter is documented for '{command}'."),
        };
    }

    /// <summary>
    /// Pulls every documented example out of the markdown. Blocks are emitted strictly as a Raw/dtk
    /// pair, so an unpaired block means the page was edited by hand into a shape the examples can no
    /// longer be verified from — which fails here rather than silently skipping the example.
    /// </summary>
    /// <param name="markdown">The full contents of an examples document.</param>
    private static List<Example> ParseExamples(string markdown)
    {
        var blocks = BlockPattern().Matches(markdown);
        var examples = new List<Example>();

        for (var i = 0; i < blocks.Count; i += 2)
        {
            var raw = blocks[i];
            var command = raw.Groups["cmd"].Value;
            raw.Groups["kind"].Value.Should().Be("Raw", "example blocks must alternate Raw then dtk");

            var filtered = blocks.Count > i + 1 ? blocks[i + 1] : null;
            filtered.Should().NotBeNull("the Raw block for '{0}' has no dtk block", command);
            filtered.Groups["kind"].Value.Should().Be("dtk", "example blocks must alternate Raw then dtk");

            // A command that succeeded carries no annotation, so an absent exit code means zero.
            var exitCode = raw.Groups["exit"].Success ? int.Parse(raw.Groups["exit"].Value, Culture) : 0;

            // Normalise line endings: the filters always emit '\n', so a CRLF checkout of the
            // markdown would otherwise fail every comparison on Windows and nowhere else.
            examples.Add(new Example(
                command,
                raw.Groups["body"].Value.ReplaceLineEndings("\n"),
                filtered.Groups["body"].Value.ReplaceLineEndings("\n"),
                exitCode));
        }

        return examples;
    }

    private static string ExamplesPath(string document)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DotnetTokenKiller.slnx")))
            {
                return Path.Combine(dir.FullName, Path.Combine(document.Split('/')));
            }
        }

        throw new InvalidOperationException(
            $"Could not locate DotnetTokenKiller.slnx above {AppContext.BaseDirectory}.");
    }

    private static System.Globalization.CultureInfo Culture => System.Globalization.CultureInfo.InvariantCulture;

    // Counts the **Raw** labels on a page, independently of whether the block after one parses.
    [GeneratedRegex(@"\*\*Raw\*\*")]
    private static partial Regex RawLabelPattern();

    // Matches a labelled example block: "**Raw** (`dotnet build ...`) — exit code 1" and its ```sh fence.
    [GeneratedRegex(
        @"\*\*(?<kind>Raw|dtk)\*\* \(`(?<cmd>[^`]+)`\)(?: — exit code (?<exit>\d+))?\r?\n\r?\n```sh\r?\n(?<body>.*?)\r?\n```",
        RegexOptions.Singleline)]
    private static partial Regex BlockPattern();

    private sealed record Example(string Command, string RawOutput, string Documented, int ExitCode);
}
