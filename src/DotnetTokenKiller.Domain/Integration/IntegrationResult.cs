namespace DotnetTokenKiller.Domain.Integration;

/// <summary>Describes which files were affected during an integration.</summary>
/// <param name="CreatedFiles">Files written for the first time.</param>
/// <param name="UpdatedFiles">Existing files that were modified.</param>
/// <param name="SkippedFiles">Existing files that were left unchanged (use --force to overwrite).</param>
public sealed record IntegrationResult(
    IReadOnlyList<string> CreatedFiles,
    IReadOnlyList<string> UpdatedFiles,
    IReadOnlyList<string> SkippedFiles);
