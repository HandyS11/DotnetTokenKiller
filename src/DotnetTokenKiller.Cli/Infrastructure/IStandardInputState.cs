namespace DotnetTokenKiller.Cli.Infrastructure;

/// <summary>Reports whether standard input is redirected.</summary>
/// <remarks>
/// An indirection over <see cref="Console.IsInputRedirected"/> so the <c>pipe</c> command's
/// terminal guard is reachable from in-process tests, which inherit the test runner's console state.
/// </remarks>
internal interface IStandardInputState
{
    /// <summary>Gets a value indicating whether standard input has been redirected.</summary>
    bool IsRedirected { get; }
}

/// <summary>The real console's standard-input state.</summary>
internal sealed class ConsoleStandardInputState : IStandardInputState
{
    /// <inheritdoc/>
    public bool IsRedirected => Console.IsInputRedirected;
}
