using System.Text;

namespace DotnetTokenKiller.Infrastructure.Tests.Tee;

/// <summary>
/// Reads a tee log file the same way <c>FileTeeLogStore.ReadBodyAsync</c> does, so tests can read a
/// log while its session's writer handle is still open.
/// </summary>
/// <remarks>
/// Plain <see cref="File.ReadAllTextAsync(string, System.Threading.CancellationToken)"/> opens with
/// <see cref="FileShare.Read"/>, which does not permit the <see cref="FileAccess.Write"/> a live
/// session's handle already holds. Windows enforces that mismatch (Linux only enforces
/// <see cref="FileShare.None"/>), so a test that reads a still-open log with the plain API passes on
/// Linux and fails on Windows with "the process cannot access the file because it is being used by
/// another process". Several tests here read a log deliberately before — or without ever —
/// finalizing its session, so this helper mirrors production's <c>FileShare.ReadWrite</c> read.
/// </remarks>
internal static class TeeLogFileReader
{
    /// <summary>Reads a tee log's full text, tolerating a still-open writer handle on Windows.</summary>
    /// <param name="path">The log file's path.</param>
    /// <returns>The file's full text.</returns>
    public static async Task<string> ReadAllTextAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }
}
