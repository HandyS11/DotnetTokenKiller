namespace DotnetTokenKiller.Domain.Files;

/// <summary>Reads the files a wrapped tool writes beside its output, such as a report it names.</summary>
public interface IArtifactFileReader
{
    /// <summary>Reads a text file.</summary>
    /// <param name="path">The file's path.</param>
    /// <returns>The file's content, or <see langword="null"/> when it is missing, unreadable or too large.</returns>
    string? TryReadText(string path);

    /// <summary>Finds the most recently written file of a name under a directory, at any depth.</summary>
    /// <param name="directory">The directory to search.</param>
    /// <param name="fileName">The file name to match, such as <c>mutation-report.json</c>.</param>
    /// <param name="notBeforeUtc">Files last written before this moment are ignored.</param>
    /// <returns>The file's full path, or <see langword="null"/> when there is none or the directory is unreadable.</returns>
    string? FindNewest(string directory, string fileName, DateTimeOffset notBeforeUtc);
}
