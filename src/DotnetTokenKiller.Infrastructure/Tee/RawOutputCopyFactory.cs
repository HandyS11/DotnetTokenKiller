using System.Security;
using DotnetTokenKiller.Domain.Tee;

namespace DotnetTokenKiller.Infrastructure.Tee;

/// <summary>Creates the file <c>--tee &lt;path&gt;</c> copies a run's raw output into.</summary>
public sealed class RawOutputCopyFactory : IRawOutputCopyFactory
{
    /// <inheritdoc/>
    public IRawOutputCopy Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        // Before the file system sees it: "   " is a legal file name on Linux, so `--tee "$UNSET"` would otherwise
        // create one, and an empty path would read as ": The value cannot be an empty string".
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new RawOutputCopyException("a blank path");
        }

        var options = new FileStreamOptions
        {
            // Create truncates an existing file, as `>` does; a missing parent directory is an error, as there.
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            // Read: the user can follow the copy (`tail -f`) while the run writes it, on Windows too.
            Share = FileShare.Read
        };

        // Owner-only (0600) on POSIX, as tee logs are: both can hold secrets from a failed command's output.
        // Applies to a file this creates; an existing file keeps its mode, as with shell redirection.
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        try
        {
            // GetFullPath resolves a relative path against the working directory, where the user typed it.
            // CA2000 cannot see that the stream's ownership transfers to the copy returned here.
#pragma warning disable CA2000
            return new RawOutputCopy(new FileStream(Path.GetFullPath(path), options));
#pragma warning restore CA2000
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException or SecurityException)
        {
            throw new RawOutputCopyException($"{path}: {ex.Message}", ex);
        }
    }
}
