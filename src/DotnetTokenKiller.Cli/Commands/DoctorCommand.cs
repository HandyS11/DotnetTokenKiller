using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Configuration;
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
    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var config = await configProvider.LoadAsync(cancellationToken).ConfigureAwait(false);

        var dbPath = Environment.GetEnvironmentVariable("DTK_DB_PATH")
                     ?? config.Tracking.DbPath
                     ?? ResolveDefaultDbPath();

        var teeDirectory = config.Tee.Directory ?? ResolveDefaultTeeDir();

        var checks = await doctorUseCase.RunAsync(dbPath, teeDirectory, cancellationToken)
            .ConfigureAwait(false);

        var allPassed = true;
        foreach (var check in checks)
        {
            var icon = check.Passed ? "[green]✔[/]" : "[red]✘[/]";
            console.MarkupLine($"  {icon}  [bold]{Markup.Escape(check.Name)}[/]: {Markup.Escape(check.Message)}");
            if (!check.Passed)
                allPassed = false;
        }

        console.WriteLine();
        if (allPassed)
            console.MarkupLine("[green]All checks passed.[/]");
        else
            console.MarkupLine("[red]Some checks failed. Review the output above.[/]");

        return allPassed ? 0 : 1;
    }

    private static string ResolveDefaultDbPath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "dtk", "tracking.db");
    }

    private static string ResolveDefaultTeeDir()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "dtk", "tee");
    }
}
