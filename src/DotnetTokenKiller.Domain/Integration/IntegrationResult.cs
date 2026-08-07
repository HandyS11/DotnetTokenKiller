namespace DotnetTokenKiller.Domain.Integration;

/// <summary>Describes which files were affected during an integration.</summary>
/// <param name="CreatedFiles">Files written for the first time.</param>
/// <param name="UpdatedFiles">Existing files that were modified.</param>
/// <param name="SkippedFiles">Existing files that were left unchanged (use --force to overwrite).</param>
/// <param name="Notes">Advisory messages explaining cross-tool actions (e.g. an rtk config edit).</param>
public sealed record IntegrationResult(
    IReadOnlyList<string> CreatedFiles,
    IReadOnlyList<string> UpdatedFiles,
    IReadOnlyList<string> SkippedFiles,
    IReadOnlyList<string> Notes)
{
    /// <summary>Creates a result with no advisory notes.</summary>
    /// <param name="createdFiles">Files written for the first time.</param>
    /// <param name="updatedFiles">Existing files that were modified.</param>
    /// <param name="skippedFiles">Existing files that were left unchanged (use --force to overwrite).</param>
    public IntegrationResult(
        IReadOnlyList<string> createdFiles,
        IReadOnlyList<string> updatedFiles,
        IReadOnlyList<string> skippedFiles)
        : this(createdFiles, updatedFiles, skippedFiles, [])
    {
    }

    /// <summary>
    /// Gets the dtk-generated files that were already current, so nothing was written.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="SkippedFiles"/>, which means "left alone because dtk could not
    /// prove it wrote this". Merging the two would make the CLI advise <c>--force</c> for a file
    /// that is already up to date, where the flag would change nothing.
    /// </remarks>
    public IReadOnlyList<string> UnchangedFiles { get; init; } = [];
}
