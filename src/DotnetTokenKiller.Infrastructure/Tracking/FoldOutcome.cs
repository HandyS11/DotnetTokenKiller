namespace DotnetTokenKiller.Infrastructure.Tracking;

/// <summary>What one <see cref="PendingRecordJournal.FoldAsync"/> did.</summary>
/// <param name="Folded"><see langword="false"/> when the lock was busy and the caller did not wait.</param>
/// <param name="Records">Records handed to the commit.</param>
/// <param name="Corrupt">Files deleted because they did not parse.</param>
internal readonly record struct FoldOutcome(bool Folded, int Records, int Corrupt);
