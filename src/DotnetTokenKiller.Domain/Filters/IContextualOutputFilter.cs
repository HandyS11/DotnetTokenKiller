namespace DotnetTokenKiller.Domain.Filters;

/// <summary>An output filter that also reads the run's context and reports verdicts about it.</summary>
public interface IContextualOutputFilter : IOutputFilter
{
    /// <summary>Applies the filter with the run's context.</summary>
    /// <remarks>
    /// Implementers make <see cref="IOutputFilter.Apply(string, int)"/> call this with
    /// <see cref="FilterContext.Unknown"/>.
    /// </remarks>
    /// <param name="strippedOutput">The command output, with ANSI escape sequences already stripped.</param>
    /// <param name="exitCode">The child's exit code.</param>
    /// <param name="context">What is known about the run.</param>
    FilterResult Apply(string strippedOutput, int exitCode, FilterContext context);
}
