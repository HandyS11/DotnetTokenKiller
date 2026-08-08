using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Infrastructure;
using DotnetTokenKiller.Infrastructure.Tee;
using DotnetTokenKiller.Infrastructure.Tracking;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Runs diagnostics to verify dtk is set up correctly.</summary>
/// <param name="doctorUseCase">The doctor use case.</param>
/// <param name="configProvider">The configuration provider.</param>
/// <param name="console">The Spectre.Console output sink.</param>
internal sealed class DoctorCommand(
    DoctorUseCase doctorUseCase,
    IConfigProvider configProvider,
    IAnsiConsole console) : AsyncCommand
{
    /// <inheritdoc/>
    protected override Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        => RunAsync(cancellationToken);

    internal async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var config = await configProvider.LoadAsync(cancellationToken).ConfigureAwait(false);

        // Resolve exactly as the DI-registered tracker and tee service do, so doctor never
        // reports a path that differs from the one dtk will actually use — including the
        // DTK_DB_PATH / DTK_TEE_DIR environment overrides.
        var dbPath = EnvironmentOverride.Read("DTK_DB_PATH")
                     ?? config.Tracking.DbPath
                     ?? SqliteTracker.GetDefaultDbPath();

        var teeDirectory = TeeDirectoryResolver.Resolve(config.Tee, null);

        var checks = await doctorUseCase.RunAsync(dbPath, teeDirectory, Environment.CurrentDirectory, cancellationToken)
            .ConfigureAwait(false);

        var allPassed = true;
        foreach (var check in checks)
        {
            var icon = check.Passed ? "[green]✔[/]" : "[red]✘[/]";
            console.MarkupLine($"  {icon}  [bold]{Markup.Escape(check.Name)}[/]: {Markup.Escape(check.Message)}");
            if (!check.Passed)
            {
                allPassed = false;
            }
        }

        console.WriteLine();
        console.MarkupLine(allPassed
            ? "[green]All checks passed.[/]"
            : "[red]Some checks failed. Review the output above.[/]");

        return allPassed ? 0 : 1;
    }
}
