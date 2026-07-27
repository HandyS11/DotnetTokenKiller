using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Domain;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Prints a shell completion script for dtk.</summary>
/// <remarks>
/// The <c>__DOTNET_CMDS_*__</c> placeholders in the templates are filled at runtime from
/// <see cref="DotnetSubcommands.Ordered"/>, so the supported dotnet subcommands
/// (build/test/restore/clean/format) can never drift between the CLI and its completions.
/// </remarks>
/// <param name="console">The Spectre.Console output sink for human-facing errors.</param>
/// <param name="output">The raw text writer for the completion script, bypassing console width wrapping.</param>
internal sealed class CompletionCommand(IAnsiConsole console, TextWriter output)
    : AsyncCommand<CompletionCommandSettings>
{
    private const string BashCompletion =
        """
        # dtk bash completion
        # To install: dtk completion bash >> ~/.bash_completion
        # or:         dtk completion bash > /etc/bash_completion.d/dtk

        _dtk_completion() {
            local cur prev words cword
            _init_completion || return

            local dotnet_cmds="__DOTNET_CMDS_BASH__"
            local integrate_providers="claude copilot gemini cursor windsurf aider jetbrains"
            local config_subcmds="show set"
            local top_cmds="dotnet integrate config doctor completion gain reset --version --help"

            case "${words[1]}" in
                dotnet)
                    if [[ $cword -eq 2 ]]; then
                        COMPREPLY=($(compgen -W "$dotnet_cmds" -- "$cur"))
                    fi
                    ;;
                integrate)
                    if [[ $cword -eq 2 ]]; then
                        COMPREPLY=($(compgen -W "$integrate_providers" -- "$cur"))
                    elif [[ $cword -ge 3 ]]; then
                        COMPREPLY=($(compgen -W "--dir --force --help" -- "$cur"))
                    fi
                    ;;
                config)
                    if [[ $cword -eq 2 ]]; then
                        COMPREPLY=($(compgen -W "$config_subcmds" -- "$cur"))
                    fi
                    ;;
                *)
                    COMPREPLY=($(compgen -W "$top_cmds" -- "$cur"))
                    ;;
            esac
        }

        complete -F _dtk_completion dtk
        """;

    private const string ZshCompletion =
        """
        #compdef dtk
        # dtk zsh completion
        #
        # To install:
        #   mkdir -p ~/.zfunc && dtk completion zsh > ~/.zfunc/_dtk
        #
        # Then add these lines to ~/.zshrc (before any compinit call):
        #   fpath=(~/.zfunc $fpath)
        #   autoload -Uz compinit && compinit
        #
        # Do NOT source this file directly in ~/.zshrc — the #compdef directive
        # requires the file to be autoloaded by zsh's completion system.

        _dtk() {
            local -a top_cmds
            top_cmds=(
                'dotnet:Run dotnet commands with filtered output'
                'integrate:Install dtk integration artifacts'
                'config:View or modify dtk configuration'
                'doctor:Run diagnostics'
                'completion:Print shell completion script'
                'gain:Show token savings analytics'
                'reset:Clear all tracking data'
            )

            local -a dotnet_cmds
            dotnet_cmds=(
                __DOTNET_CMDS_ZSH__
            )

            local -a integrate_providers
            integrate_providers=(
                'claude:Install dtk skill and hook for Claude Code'
                'copilot:Install dtk instructions for GitHub Copilot'
                'gemini:Install dtk instructions and hook for Gemini CLI'
                'cursor:Install dtk rules for Cursor'
                'windsurf:Install dtk rules for Windsurf'
                'aider:Install dtk rules for Aider'
                'jetbrains:Install dtk guidelines for JetBrains AI'
            )

            local -a config_cmds
            config_cmds=(
                'show:Display the current configuration'
                'set:Set a configuration value'
            )

            local -a shells
            shells=(bash zsh fish powershell)

            case $words[2] in
                dotnet)
                    _describe 'dotnet subcommand' dotnet_cmds
                    ;;
                integrate)
                    _describe 'integration provider' integrate_providers
                    ;;
                config)
                    _describe 'config subcommand' config_cmds
                    ;;
                completion)
                    _describe 'shell' shells
                    ;;
                *)
                    _describe 'command' top_cmds
                    ;;
            esac
        }
        """;

    private const string FishCompletion =
        """
        # dtk fish completion
        # To install: dtk completion fish > ~/.config/fish/completions/dtk.fish

        # Top-level subcommands
        complete -c dtk -f -n '__fish_use_subcommand' -a dotnet     -d 'Run dotnet commands with filtered output'
        complete -c dtk -f -n '__fish_use_subcommand' -a integrate   -d 'Install dtk integration artifacts'
        complete -c dtk -f -n '__fish_use_subcommand' -a config      -d 'View or modify dtk configuration'
        complete -c dtk -f -n '__fish_use_subcommand' -a doctor      -d 'Run diagnostics'
        complete -c dtk -f -n '__fish_use_subcommand' -a completion  -d 'Print shell completion script'
        complete -c dtk -f -n '__fish_use_subcommand' -a gain        -d 'Show token savings analytics'
        complete -c dtk -f -n '__fish_use_subcommand' -a reset       -d 'Clear all tracking data'

        # dotnet subcommands
        __DOTNET_CMDS_FISH__

        # integrate subcommands
        complete -c dtk -f -n '__fish_seen_subcommand_from integrate' -a claude    -d 'Install dtk skill and hook for Claude Code'
        complete -c dtk -f -n '__fish_seen_subcommand_from integrate' -a copilot   -d 'Install dtk instructions for GitHub Copilot'
        complete -c dtk -f -n '__fish_seen_subcommand_from integrate' -a gemini    -d 'Install dtk instructions and hook for Gemini CLI'
        complete -c dtk -f -n '__fish_seen_subcommand_from integrate' -a cursor    -d 'Install dtk rules for Cursor'
        complete -c dtk -f -n '__fish_seen_subcommand_from integrate' -a windsurf  -d 'Install dtk rules for Windsurf'
        complete -c dtk -f -n '__fish_seen_subcommand_from integrate' -a aider     -d 'Install dtk rules for Aider'
        complete -c dtk -f -n '__fish_seen_subcommand_from integrate' -a jetbrains -d 'Install dtk guidelines for JetBrains AI'

        # config subcommands
        complete -c dtk -f -n '__fish_seen_subcommand_from config' -a show -d 'Display the current configuration'
        complete -c dtk -f -n '__fish_seen_subcommand_from config' -a set  -d 'Set a configuration value'

        # completion shells
        complete -c dtk -f -n '__fish_seen_subcommand_from completion' -a bash       -d 'Bash completion script'
        complete -c dtk -f -n '__fish_seen_subcommand_from completion' -a zsh        -d 'Zsh completion script'
        complete -c dtk -f -n '__fish_seen_subcommand_from completion' -a fish       -d 'Fish completion script'
        complete -c dtk -f -n '__fish_seen_subcommand_from completion' -a powershell -d 'PowerShell completion script'
        """;

    private const string PowerShellCompletion =
        """
        # dtk PowerShell completion
        # To install: dtk completion powershell >> $PROFILE

        Register-ArgumentCompleter -Native -CommandName dtk -ScriptBlock {
            param($wordToComplete, $commandAst, $cursorPosition)

            $tokens = $commandAst.CommandElements | Select-Object -Skip 1 | ForEach-Object { $_.ToString() }
            $count = $tokens.Count

            $topCmds = @('dotnet', 'integrate', 'config', 'doctor', 'completion', 'gain', 'reset')
            $dotnetCmds = @(__DOTNET_CMDS_PS__)
            $providers = @('claude', 'copilot', 'gemini', 'cursor', 'windsurf', 'aider', 'jetbrains')
            $configCmds = @('show', 'set')
            $shells = @('bash', 'zsh', 'fish', 'powershell')

            $candidates = switch ($tokens[0]) {
                'dotnet'    { if ($count -eq 1) { $dotnetCmds } }
                'integrate' { if ($count -eq 1) { $providers } }
                'config'    { if ($count -eq 1) { $configCmds } }
                'completion'{ if ($count -eq 1) { $shells } }
                default     { $topCmds }
            }

            $candidates |
                Where-Object { $_ -like "$wordToComplete*" } |
                ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                }
        }
        """;

    /// <summary>Space-separated dotnet subcommand list for the bash script.</summary>
    private static readonly string BashDotnetCommands =
        string.Join(' ', DotnetSubcommands.Ordered);

    private static readonly string PowerShellDotnetCommands =
        string.Join(", ", DotnetSubcommands.Ordered.Select(sub => $"'{sub}'"));

    private static readonly string ZshDotnetCommands =
        string.Join(
            "\n        ",
            DotnetSubcommands.Ordered.Select(
                sub => $"'{sub}:Run dotnet {sub} with filtered output'"));

    private static readonly string FishDotnetCommands =
        string.Join(
            '\n',
            DotnetSubcommands.Ordered.Select(
                sub => $"complete -c dtk -f -n '__fish_seen_subcommand_from dotnet' -a {sub} -d 'Run dotnet {sub} with filtered output'"));

    /// <inheritdoc/>
    protected override Task<int> ExecuteAsync(
        CommandContext context,
        CompletionCommandSettings settings,
        CancellationToken cancellationToken)
        => RunAsync(settings, cancellationToken);

    internal async Task<int> RunAsync(
        CompletionCommandSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var script = settings.Shell.ToLowerInvariant() switch
        {
            "bash" => BashCompletion.Replace("__DOTNET_CMDS_BASH__", BashDotnetCommands, StringComparison.Ordinal),
            "zsh" => ZshCompletion.Replace("__DOTNET_CMDS_ZSH__", ZshDotnetCommands, StringComparison.Ordinal),
            "fish" => FishCompletion.Replace("__DOTNET_CMDS_FISH__", FishDotnetCommands, StringComparison.Ordinal),
            "powershell" or "pwsh" =>
                PowerShellCompletion.Replace("__DOTNET_CMDS_PS__", PowerShellDotnetCommands, StringComparison.Ordinal),
            _ => null
        };

        if (script is null)
        {
            console.MarkupLine(
                $"[red]Error:[/] Unknown shell '[bold]{Markup.Escape(settings.Shell)}[/]'. " +
                "Supported shells: bash, zsh, fish, powershell");
            return 1;
        }

        await output.WriteLineAsync(script.AsMemory(), cancellationToken).ConfigureAwait(false);
        return 0;
    }
}
