namespace DotnetTokenKiller.Domain.Filters;

/// <summary>A filter that needs to know which files a run changed, found by hashing a tree before and after it.</summary>
public interface ISnapshotFilter
{
    /// <summary>The directory to snapshot for this run.</summary>
    /// <param name="arguments">The child's arguments after the subcommand's tokens.</param>
    /// <param name="workingDirectory">The run's working directory.</param>
    string SnapshotRoot(IReadOnlyList<string> arguments, string workingDirectory);

    /// <summary>
    /// Whether the run can change a file of this name. The snapshot hashes only those, so files the tool never
    /// touches (sample data, images, binaries) cost no read and do not push the tree over the snapshot's budget.
    /// </summary>
    /// <param name="fileName">A file name, without its directory.</param>
    bool CanChange(string fileName);
}
