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

        return Render(console, checks);
    }

    /// <summary>Prints one line per check and a summary, and returns doctor's exit code.</summary>
    /// <param name="console">The output sink.</param>
    /// <param name="checks">The checks to print.</param>
    /// <returns>1 when any check failed, otherwise 0; warnings do not count as failures.</returns>
    internal static int Render(IAnsiConsole console, IReadOnlyList<DiagnosticCheck> checks)
    {
        foreach (var check in checks)
        {
            var icon = (check.Passed, check.IsWarning) switch
            {
                (false, _) => "[red]✘[/]",
                (true, true) => "[yellow]![/]",
                _ => "[green]✔[/]"
            };
            console.MarkupLine($"  {icon}  [bold]{Markup.Escape(check.Name)}[/]: {Markup.Escape(check.Message)}");
        }

        var failed = checks.Count(check => !check.Passed);
        var warnings = checks.Count(check => check.Passed && check.IsWarning);

        console.WriteLine();
        console.MarkupLine((failed, warnings) switch
        {
            ( > 0, _) => "[red]Some checks failed. Review the output above.[/]",
            (_, > 0) => $"[green]All checks passed[/][yellow], with {warnings} warning(s).[/]",
            _ => "[green]All checks passed.[/]"
        });

        return failed > 0 ? 1 : 0;
    }
}
