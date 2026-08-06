namespace DotnetTokenKiller.Domain.Tracking;

/// <summary>A record of a single command execution and its token statistics.</summary>
public sealed record CommandRecord
{
    /// <summary>Initializes a new instance of the <see cref="CommandRecord"/> class.</summary>
    /// <param name="timestamp">When the command ran.</param>
    /// <param name="command">The dotnet subcommand name (must not be empty or whitespace).</param>
    /// <param name="projectPath">The working directory when the command ran.</param>
    /// <param name="tokens">Token usage statistics for the run.</param>
    /// <param name="executionTime">Total wall-clock time for the command.</param>
    /// <remarks>
    /// <see cref="Success"/>, <see cref="Outcome"/>, and <see cref="Source"/> are set through an
    /// object initializer rather than as constructor arguments: they carry defaults that describe
    /// the overwhelmingly common case (a successful, filtered, dtk-run command), and three trailing
    /// optional parameters push the constructor past what a call site can read positionally.
    /// </remarks>
    public CommandRecord(
        DateTimeOffset timestamp,
        string command,
        string projectPath,
        TokenStatistics tokens,
        TimeSpan executionTime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(tokens);

        Timestamp = timestamp;
        Command = command;
        ProjectPath = projectPath;
        InputTokens = tokens.Input;
        OutputTokens = tokens.Output;
        SavedTokens = tokens.Saved;
        SavingsPercentage = tokens.SavingsPercentage;
        ExecutionTime = executionTime;
    }

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

    /// <summary>Whether the command exited with code 0.</summary>
    public bool Success { get; init; } = true;

    /// <summary>How the run produced its output.</summary>
    public RunOutcome Outcome { get; init; } = RunOutcome.Filtered;

    /// <summary>Where the filtered raw output came from.</summary>
    public RunSource Source { get; init; } = RunSource.Run;
}
