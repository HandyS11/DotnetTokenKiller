namespace DotnetTokenKiller.Domain.Tee;

/// <summary>Creates the file <c>--tee &lt;path&gt;</c> copies a run's raw output into.</summary>
public interface IRawOutputCopyFactory
{
    /// <summary>
    /// Creates <paramref name="path"/>, or truncates it when it exists, ready for a run that is about to start.
    /// A relative path is resolved against the working directory, and a missing parent directory is not created
    /// (as with shell redirection). A new file is owner-only on POSIX, like a tee log, since both can hold secrets.
    /// </summary>
    /// <param name="path">The path as the user gave it.</param>
    /// <returns>The open copy; the caller disposes it once the run's output has all been written.</returns>
    /// <exception cref="RawOutputCopyException">
    /// The file cannot be created; the message is <c>&lt;path&gt;: &lt;reason&gt;</c>, naming the path as given and
    /// the operating system's reason.
    /// </exception>
    IRawOutputCopy Open(string path);
}
