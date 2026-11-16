using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Domain;
using DotnetTokenKiller.Domain.Filters;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Runs dotnet list package with filtered output.</summary>
/// <param name="filteredRun">The filtered run use case.</param>
/// <param name="filter">The list package output filter.</param>
internal sealed class DotnetListPackageCommand(
    FilteredRunUseCase filteredRun,
    [FromKeyedServices(FilterKeys.ListPackage)]
    IOutputFilter filter) : AsyncCommand<DotnetCommandSettings>
{
    /// <inheritdoc/>
    public override Task<int> ExecuteAsync(CommandContext context, DotnetCommandSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);

        var args = DotnetArguments([.. settings.PositionalArgs.Concat(context.Remaining.Raw)]);

        return filteredRun.RunAsync(filter, "dotnet", args, settings.ToOutputOptions(), cancellationToken: cancellationToken);
    }

    /// <summary>
    /// The child's arguments, <c>list package …</c>, with both words prepended, since FilteredRunUseCase derives
    /// the tracking slug by matching this same argument list. A leading project or solution goes between the words,
    /// where dotnet takes it (<c>dotnet list ISDM.slnx package</c>; it rejects one after <c>package</c>), whether it
    /// was typed there, and moved behind the words for routing by <c>ArgumentPreprocessor.InsertSeparator</c>, or
    /// typed after them.
    /// </summary>
    /// <param name="rest">The arguments after <c>list package</c>.</param>
    internal static string[] DotnetArguments(IReadOnlyList<string> rest)
    {
        ArgumentNullException.ThrowIfNull(rest);

        return rest.Count > 0 && DotnetSubcommands.IsTarget(rest[0])
            ? ["list", rest[0], "package", .. rest.Skip(1)]
            : ["list", "package", .. rest];
    }
}
