namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

/// <summary>One dtk invocation inside a parity case.</summary>
/// <param name="Arguments">Arguments for dtk. <c>{project}</c> is replaced with the sandbox's project
/// directory.</param>
/// <param name="StdinFixture">A fixture file name piped to standard input, or <see langword="null"/>
/// for an empty, closed standard input.</param>
internal sealed record ParityStep(string[] Arguments, string? StdinFixture = null);

/// <summary>A sequence of dtk invocations sharing one sandbox, compared as a whole.</summary>
/// <param name="Steps">The invocations, run in order.</param>
/// <param name="Arrange">Prepares the sandbox before the first step, or <see langword="null"/>.</param>
internal sealed record ParityCase(IReadOnlyList<ParityStep> Steps, Action<ParitySandbox>? Arrange = null);
