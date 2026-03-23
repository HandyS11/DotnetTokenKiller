using DotnetTokenKiller.Domain.Configuration;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Globalization;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Displays the current dtk configuration.</summary>
/// <param name="configProvider">The configuration provider.</param>
/// <param name="console">The Spectre.Console output sink.</param>
internal sealed class ConfigShowCommand(
    IConfigProvider configProvider,
    IAnsiConsole console) : AsyncCommand
{
    /// <inheritdoc/>
    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var config = await configProvider.LoadAsync(cancellationToken).ConfigureAwait(false);

        var table = new Table()
            .AddColumn("Key")
            .AddColumn("Value");

        table.AddRow("tracking.enabled", config.Tracking.Enabled.ToString(CultureInfo.InvariantCulture));
        table.AddRow("tracking.retentionDays", config.Tracking.RetentionDays.ToString(CultureInfo.InvariantCulture));
        table.AddRow("tracking.dbPath", config.Tracking.DbPath ?? "[grey](default)[/]");
        table.AddRow("tracking.tokenizer", config.Tracking.Tokenizer.ToString());
        table.AddRow("display.colors", config.Display.Colors.ToString(CultureInfo.InvariantCulture));
        table.AddRow("display.emoji", config.Display.Emoji.ToString(CultureInfo.InvariantCulture));
        table.AddRow("display.width", config.Display.Width.ToString(CultureInfo.InvariantCulture));
        table.AddRow("tee.mode", config.Tee.Mode.ToString());
        table.AddRow("tee.directory", config.Tee.Directory ?? "[grey](default)[/]");
        table.AddRow("tee.maxFiles", config.Tee.MaxFiles.ToString(CultureInfo.InvariantCulture));
        table.AddRow("tee.maxFileSizeBytes", config.Tee.MaxFileSizeBytes.ToString(CultureInfo.InvariantCulture));

        console.Write(table);
        return 0;
    }
}
