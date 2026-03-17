namespace DotnetTokenKiller.Domain.Tracking;

/// <summary>A record of a single command execution and its token statistics.</summary>
public sealed record CommandRecord
{
    /// <summary>When the command ran.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>The dotnet subcommand name (must not be empty or whitespace).</summary>
    public string Command { get; init; }

    /// <summary>The working directory when the command ran.</summary>
    public string ProjectPath { get; init; }

    /// <summary>Estimated tokens in the raw output.</summary>
    public int InputTokens { get; init; }

    /// <summary>Estimated tokens in the filtered output.</summary>
    public int OutputTokens { get; init; }

    /// <summary>Tokens saved by filtering.</summary>
    public int SavedTokens { get; init; }

    /// <summary>Percentage of tokens saved.</summary>
    public double SavingsPercentage { get; init; }

    /// <summary>Total wall-clock time for the command.</summary>
    public TimeSpan ExecutionTime { get; init; }

    /// <summary>Initializes a new instance of the <see cref="CommandRecord"/> class.</summary>
    /// <param name="timestamp">When the command ran.</param>
    /// <param name="command">The dotnet subcommand name (must not be empty or whitespace).</param>
    /// <param name="projectPath">The working directory when the command ran.</param>
    /// <param name="inputTokens">Estimated tokens in the raw output.</param>
    /// <param name="outputTokens">Estimated tokens in the filtered output.</param>
    /// <param name="savedTokens">Tokens saved by filtering.</param>
    /// <param name="savingsPercentage">Percentage of tokens saved.</param>
    /// <param name="executionTime">Total wall-clock time for the command.</param>
    public CommandRecord(
        DateTimeOffset timestamp,
        string command,
        string projectPath,
        int inputTokens,
        int outputTokens,
        int savedTokens,
        double savingsPercentage,
        TimeSpan executionTime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);

        Timestamp = timestamp;
        Command = command;
        ProjectPath = projectPath;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        SavedTokens = savedTokens;
        SavingsPercentage = savingsPercentage;
        ExecutionTime = executionTime;
    }
}
