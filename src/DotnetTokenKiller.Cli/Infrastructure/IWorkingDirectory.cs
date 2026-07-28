namespace DotnetTokenKiller.Cli.Infrastructure;

/// <summary>Reports the process's current working directory.</summary>
/// <remarks>
/// An indirection over <see cref="Environment.CurrentDirectory"/> so <c>dtk log</c>'s project
/// scoping is reachable from in-process tests. Mutating the real value would be process-global and
/// would race with xunit's parallel collections.
/// </remarks>
internal interface IWorkingDirectory
{
    /// <summary>Gets the current working directory.</summary>
    string Current { get; }
}

/// <summary>The real process working directory.</summary>
internal sealed class ProcessWorkingDirectory : IWorkingDirectory
{
    /// <inheritdoc/>
    public string Current => Environment.CurrentDirectory;
}
