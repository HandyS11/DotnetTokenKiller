namespace DotnetTokenKiller.Domain.Tee;

public interface ITeeService
{
    Task<string?> TeeAndHintAsync(
        string rawOutput,
        string commandSlug,
        int exitCode,
        CancellationToken cancellationToken = default);
}
