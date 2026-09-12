using System.Text.Json;

namespace DotnetTokenKiller.Benchmarks.Corpus.Savings;

/// <summary>Reads and writes the committed savings baseline.</summary>
public static class SavingsBaselineFile
{
    /// <summary>The command that regenerates the baseline, quoted in drift reports.</summary>
    public const string RegenerateCommand =
        "dotnet run -c Release --project benchmarks/DotnetTokenKiller.Benchmarks -- update-baseline";

    private const string RelativePath =
        "benchmarks/DotnetTokenKiller.Benchmarks.Corpus/Baselines/savings-baseline.json";

    /// <summary>Resolves the baseline's absolute path by walking up to the repository root.</summary>
    /// <remarks>
    /// The same walk <c>ExamplesBindingTests.ExamplesPath</c> uses. The file is read from the
    /// working tree rather than from an embedded copy, so a regenerated baseline is reviewable as a
    /// diff and the reader and the writer cannot disagree about which copy is authoritative.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The repository root was not found.</exception>
    public static string Path()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "DotnetTokenKiller.slnx")))
            {
                return System.IO.Path.Combine(
                    dir.FullName, System.IO.Path.Combine(RelativePath.Split('/')));
            }
        }

        throw new InvalidOperationException(
            $"Could not locate DotnetTokenKiller.slnx above {AppContext.BaseDirectory}.");
    }

    /// <summary>Serializes a baseline to its committed JSON form.</summary>
    /// <param name="baseline">The baseline to serialize.</param>
    public static string Serialize(SavingsBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        return JsonSerializer.Serialize(baseline, SavingsBaselineJsonContext.Default.SavingsBaseline);
    }

    /// <summary>Deserializes a baseline from its committed JSON form.</summary>
    /// <param name="json">The JSON to read.</param>
    /// <exception cref="InvalidOperationException">The JSON did not describe a baseline.</exception>
    public static SavingsBaseline Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return JsonSerializer.Deserialize(json, SavingsBaselineJsonContext.Default.SavingsBaseline)
               ?? throw new InvalidOperationException("The savings baseline JSON was null.");
    }

    /// <summary>Reads the committed baseline.</summary>
    /// <exception cref="InvalidOperationException">The baseline file does not exist.</exception>
    public static SavingsBaseline Read()
    {
        var path = Path();

        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"No savings baseline at {path}. Generate one with: {RegenerateCommand}");
        }

        return Deserialize(File.ReadAllText(path));
    }

    /// <summary>Writes the baseline, creating its directory if needed.</summary>
    /// <param name="baseline">The baseline to write.</param>
    public static void Write(SavingsBaseline baseline)
    {
        var path = Path();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        // Trailing newline, LF endings: .gitattributes and .editorconfig require both, and a file
        // written without them fails the formatting check rather than the test.
        File.WriteAllText(path, Serialize(baseline).ReplaceLineEndings("\n") + "\n");
    }
}
