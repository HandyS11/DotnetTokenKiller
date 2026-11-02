namespace DotnetTokenKiller.Application.Tests.UseCases;

/// <summary>
/// The tests that set, or assert glyphs that depend on, the process-wide NO_COLOR variable. See
/// <see cref="NoColorCollectionTests"/>.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public static class NoColorEnvironment
{
    /// <summary>The collection's name, as <c>[Collection]</c> and <c>[CollectionDefinition]</c> spell it.</summary>
    public const string Name = "NoColorEnvironment";
}
