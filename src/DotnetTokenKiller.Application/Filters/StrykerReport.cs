using System.Text.Json;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>One mutant of a Stryker mutation report.</summary>
/// <param name="File">The mutated file's path, as the report gives it.</param>
/// <param name="Line">The mutant's first line, 1-based.</param>
/// <param name="Column">The mutant's first column, 1-based.</param>
/// <param name="MutatorName">The mutator, such as <c>Equality mutation</c>.</param>
/// <param name="Replacement">The code the mutant puts in place, when the report gives it.</param>
/// <param name="Status">The mutant's status, such as <c>Killed</c>, <c>Survived</c> or <c>NoCoverage</c>.</param>
public sealed record StrykerMutant(string File, int Line, int Column, string MutatorName, string? Replacement,
    string Status);

/// <summary>Reads Stryker's json report (the mutation-testing-elements schema).</summary>
/// <remarks>
/// Only <c>files.&lt;path&gt;.mutants[]</c> is read: each mutant's <c>mutatorName</c>, <c>replacement</c>,
/// <c>location.start</c> and <c>status</c>. A mutant without a status or a start location is skipped.
/// </remarks>
public static class StrykerReport
{
    /// <summary>Reads every mutant of a report.</summary>
    /// <param name="json">The report's text.</param>
    /// <returns>The mutants, in the report's order, or an empty list when the text is not a report.</returns>
    public static IReadOnlyList<StrykerMutant> ReadMutants(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var mutants = new List<StrykerMutant>();
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("files", out var files)
                && files.ValueKind == JsonValueKind.Object)
            {
                foreach (var file in files.EnumerateObject())
                {
                    AddMutants(file.Name, file.Value, mutants);
                }
            }

            return mutants;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void AddMutants(string path, JsonElement file, List<StrykerMutant> mutants)
    {
        if (file.ValueKind != JsonValueKind.Object
            || !file.TryGetProperty("mutants", out var list)
            || list.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var mutant in list.EnumerateArray())
        {
            if (TryRead(path, mutant) is { } read)
            {
                mutants.Add(read);
            }
        }
    }

    private static StrykerMutant? TryRead(string path, JsonElement mutant)
    {
        if (mutant.ValueKind != JsonValueKind.Object
            || StringProperty(mutant, "status") is not { } status
            || !mutant.TryGetProperty("location", out var location)
            || location.ValueKind != JsonValueKind.Object
            || !location.TryGetProperty("start", out var start)
            || IntProperty(start, "line") is not { } line
            || IntProperty(start, "column") is not { } column)
        {
            return null;
        }

        return new StrykerMutant(path, line, column, StringProperty(mutant, "mutatorName") ?? string.Empty,
            StringProperty(mutant, "replacement"), status);
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? IntProperty(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;
}
