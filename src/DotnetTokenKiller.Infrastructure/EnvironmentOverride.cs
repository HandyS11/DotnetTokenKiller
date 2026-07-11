namespace DotnetTokenKiller.Infrastructure;

/// <summary>Reads dtk environment-variable overrides consistently across the infrastructure layer.</summary>
public static class EnvironmentOverride
{
    /// <summary>
    /// Reads an environment variable, returning <see langword="null"/> when it is unset, empty, or
    /// whitespace, and the trimmed value otherwise. This keeps a blank override from being treated
    /// as a real (empty) path.
    /// </summary>
    /// <param name="name">The environment variable name.</param>
    /// <returns>The trimmed value, or <see langword="null"/> when unset or blank.</returns>
    public static string? Read(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
