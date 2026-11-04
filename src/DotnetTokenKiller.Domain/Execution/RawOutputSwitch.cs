namespace DotnetTokenKiller.Domain.Execution;

/// <summary>
/// <c>DTK_RAW</c>, the switch that asks for a dotnet command's own output, unfiltered: dtk honours it at run time,
/// and the harness hooks leave a <c>dotnet</c> behind a <c>DTK_RAW=</c> assignment that turns it on unrewritten.
/// </summary>
public static class RawOutputSwitch
{
    /// <summary>The environment variable's name.</summary>
    public const string EnvironmentVariable = "DTK_RAW";

    /// <summary>
    /// Whether <paramref name="value"/> turns raw output on: any non-empty value except <c>0</c> and <c>false</c>
    /// (ignoring case).
    /// </summary>
    /// <param name="value">The variable's value, or <see langword="null"/> when it is unset.</param>
    public static bool IsOnFor(string? value) =>
        !string.IsNullOrEmpty(value)
        && !string.Equals(value, "0", StringComparison.Ordinal)
        && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
}
