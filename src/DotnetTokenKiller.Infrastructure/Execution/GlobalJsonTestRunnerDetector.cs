using System.Security;
using System.Text.Json;
using DotnetTokenKiller.Domain.Execution;

namespace DotnetTokenKiller.Infrastructure.Execution;

/// <summary>
/// Reads the runner <c>dotnet test</c> uses from the nearest <c>global.json</c>, as the SDK does: the first one
/// found walking up from the working directory decides, and its <c>"test": {"runner": …}</c> opts into
/// Microsoft.Testing.Platform. Without one, or without that setting, the run uses VSTest.
/// </summary>
/// <remarks>
/// Matches SDK 10.0.403: the property names are case-sensitive (<c>"Test"</c> leaves VSTest on) and the runner's
/// value is not (<c>"microsoft.testing.platform"</c> opts in). A file that cannot be read or parsed, or whose
/// <c>test</c> section has an unexpected shape, gives <see cref="TestRunner.Unknown"/>: dtk must never add an
/// argument for the wrong runner, since each one's is an error or means something else under the other.
/// </remarks>
public sealed class GlobalJsonTestRunnerDetector : ITestRunnerDetector
{
    private const string FileName = "global.json";
    private const string MicrosoftTestingPlatform = "Microsoft.Testing.Platform";

    /// <summary>Comments and trailing commas are read, so that a hand-edited file is not taken for a broken one.</summary>
    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <inheritdoc/>
    public TestRunner Detect(string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(workingDirectory);

        for (var directory = Path.GetFullPath(workingDirectory);
             directory is not null;
             directory = Path.GetDirectoryName(directory))
        {
            var path = Path.Combine(directory, FileName);
            if (File.Exists(path))
            {
                return ReadRunner(path);
            }
        }

        return TestRunner.VsTest;
    }

    private static TestRunner ReadRunner(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path), ReadOptions);
            return RunnerOf(document.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException
                                       or SecurityException)
        {
            return TestRunner.Unknown;
        }
    }

    private static TestRunner RunnerOf(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return TestRunner.Unknown;
        }

        if (!root.TryGetProperty("test", out var test))
        {
            return TestRunner.VsTest;
        }

        if (test.ValueKind != JsonValueKind.Object)
        {
            return TestRunner.Unknown;
        }

        if (!test.TryGetProperty("runner", out var runner))
        {
            return TestRunner.VsTest;
        }

        if (runner.ValueKind != JsonValueKind.String)
        {
            return TestRunner.Unknown;
        }

        return string.Equals(runner.GetString(), MicrosoftTestingPlatform, StringComparison.OrdinalIgnoreCase)
            ? TestRunner.MicrosoftTestingPlatform
            : TestRunner.VsTest;
    }
}
