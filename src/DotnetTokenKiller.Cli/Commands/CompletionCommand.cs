using DotnetTokenKiller.Cli.Commands.Settings;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

/// <summary>Prints a shell completion script for dtk.</summary>
/// <param name="console">The Spectre.Console output sink.</param>
internal sealed class CompletionCommand(IAnsiConsole console) : AsyncCommand<CompletionCommandSettings>
{
    /// <inheritdoc/>
    public override Task<int> ExecuteAsync(
        CommandContext context,
        CompletionCommandSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var script = settings.Shell.ToLowerInvariant() switch
        {
            "bash" => BashCompletion,
            "zsh" => ZshCompletion,
            "fish" => FishCompletion,
            "powershell" or "pwsh" => PowerShellCompletion,
            _ => null
        };

        if (script is null)
        {
            console.MarkupLine(
                $"[red]Error:[/] Unknown shell '[bold]{Markup.Escape(settings.Shell)}[/]'. " +
                "Supported shells: bash, zsh, fish, powershell");
            return Task.FromResult(1);
        }

        console.WriteLine(script);
        return Task.FromResult(0);
    }

    private const string BashCompletion =
        """
        # dtk bash completion
        # To install: dtk completion bash >> ~/.bash_completion
        # or:         dtk completion bash > /etc/bash_completion.d/dtk

        _dtk_completion() {
            local cur prev words cword
            _init_completion || return

            local dotnet_cmds="build test restore clean"
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
        # To install: dtk completion zsh > "${fpath[1]}/_dtk"

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
                'build:Run dotnet build with filtered output'
                'test:Run dotnet test with filtered output'
                'restore:Run dotnet restore with filtered output'
                'clean:Run dotnet clean with filtered output'
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

        _dtk
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
        complete -c dtk -f -n '__fish_seen_subcommand_from dotnet' -a build   -d 'Run dotnet build with filtered output'
        complete -c dtk -f -n '__fish_seen_subcommand_from dotnet' -a test    -d 'Run dotnet test with filtered output'
        complete -c dtk -f -n '__fish_seen_subcommand_from dotnet' -a restore -d 'Run dotnet restore with filtered output'
        complete -c dtk -f -n '__fish_seen_subcommand_from dotnet' -a clean   -d 'Run dotnet clean with filtered output'

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
            $dotnetCmds = @('build', 'test', 'restore', 'clean')
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
}
