using DotnetTokenKiller.Application.Integration;
using Spectre.Console;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Installs dtk integration artifacts for Cursor.</summary>
/// <param name="integrateUseCase">The integration use case.</param>
/// <param name="console">The Spectre.Console output sink.</param>
internal sealed class CursorIntegrateCommand(IntegrateUseCase integrateUseCase, IAnsiConsole console)
    : IntegrateCommandBase(integrateUseCase, console)
{
    /// <inheritdoc/>
    protected override string ProviderName => "cursor";
}
