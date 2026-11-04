namespace DotnetTokenKiller.Domain.Files;

/// <summary>Hashes the files under a directory so a run's content changes can be told from its rewrites.</summary>
public interface IFileContentSnapshotter
{
    /// <summary>Takes a snapshot of the tree under <paramref name="root"/>.</summary>
    /// <param name="root">The directory to walk.</param>
    /// <param name="cancellationToken">Checked between files.</param>
    /// <returns>The snapshot, or <see langword="null"/> when the tree is too large or cannot be walked.</returns>
    FileContentSnapshot? Take(string root, CancellationToken cancellationToken = default);
}
