namespace DotnetTokenKiller.Domain.Files;

/// <summary>What git says about the files of the work tree a directory belongs to.</summary>
/// <remarks>
/// Every answer is <see langword="null"/> when the directory is not in a git work tree, git cannot be run, or it does
/// not answer in time: a caller then does without, as it would outside a repository.
/// </remarks>
public interface IGitWorkingTree
{
    /// <summary>
    /// The files under <paramref name="directory"/> that git does not ignore: tracked ones, and untracked ones no
    /// ignore rule matches. Paths are relative to <paramref name="directory"/>, <c>/</c>-separated, and may name a
    /// tracked file that no longer exists.
    /// </summary>
    /// <param name="directory">A directory in the work tree.</param>
    /// <param name="cancellationToken">Stops git.</param>
    IReadOnlyList<string>? UnignoredFiles(string directory, CancellationToken cancellationToken = default);

    /// <summary>
    /// The files the work in progress changed, as full paths: those that differ from the default branch where the
    /// current one left it (the merge base with <c>origin/HEAD</c>), committed or not, and untracked files no
    /// ignore rule matches. <see langword="null"/> when there is no such merge base (no <c>origin/HEAD</c>, as after
    /// <c>git remote add</c>; one naming a missing branch; no commit yet): the changes not committed yet would leave
    /// out what the branch committed.
    /// </summary>
    /// <param name="directory">A directory in the work tree; the answer covers the whole work tree.</param>
    /// <param name="cancellationToken">Stops git.</param>
    IReadOnlyList<string>? ChangedFiles(string directory, CancellationToken cancellationToken = default);
}
