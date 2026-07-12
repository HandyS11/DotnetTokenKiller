using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Claude Code.</summary>
/// <param name="rtk">Detects and reconciles an rtk hook so dtk owns dotnet commands.</param>
/// <param name="home">Resolves the user's home directory for global (home-config) integration.</param>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>.claude/skills/dotnet-token-killer/SKILL.md</c></description></item>
///   <item><description><c>.claude/hooks/dotnet-to-dtk.py</c></description></item>
///   <item><description><c>.claude/settings.json</c> (merged, never overwritten)</description></item>
///   <item><description>
///     When an rtk PreToolUse hook is detected, merges <c>exclude_commands = ["dotnet"]</c> into
///     <c>~/.config/rtk/config.toml</c> so dtk (not rtk) owns dotnet commands. Silent if already excluded.
///   </description></item>
/// </list>
/// Declared <see langword="internal"/> (rather than <see langword="public"/>, its original
/// accessibility) because its primary constructor takes the <see langword="internal"/>
/// <see cref="RtkHookCoexistence"/>: a primary constructor is as accessible as its containing
/// type, and the compiler rejects (CS0051) a public constructor exposing a less-accessible
/// parameter type. Keeping <see cref="RtkHookCoexistence"/> internal (rather than promoting it to
/// public) requires this type to be internal too; callers still reach it polymorphically through
/// the public <see cref="IProviderIntegrator"/> via DI, and tests reach it directly via
/// <c>InternalsVisibleTo</c>.
/// </remarks>
internal sealed class ClaudeCodeIntegrator(RtkHookCoexistence rtk, HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator
{
    /// <summary>
    /// Quoted and rooted at <c>$CLAUDE_PROJECT_DIR</c> (the absolute project root Claude Code
    /// exports to hooks) so the hook resolves correctly regardless of Claude's current working
    /// directory.
    /// </summary>
    private const string HookCommand = """python3 "$CLAUDE_PROJECT_DIR"/.claude/hooks/dotnet-to-dtk.py""";

    /// <summary>
    /// Global variant of <see cref="HookCommand"/>: rooted at <c>$HOME</c> because the hook script is
    /// installed under <c>~/.claude/hooks</c> (Claude's <c>$CLAUDE_PROJECT_DIR</c> points at the
    /// current project, not the home-installed script).
    /// </summary>
    private const string GlobalHookCommand = """python3 "$HOME"/.claude/hooks/dotnet-to-dtk.py""";

    private const string SkillMarkdown =
        """
        ---
        name: dotnet-token-killer
        description: 'Use `dtk` (DotnetTokenKiller) instead of raw `dotnet` commands to reduce token usage when building, testing, restoring, cleaning, or formatting .NET projects.'
        ---

        # DotnetTokenKiller (dtk)

        `dtk` wraps `dotnet` commands and filters output to actionable signal only, saving 50-97% of tokens by stripping SDK banners, MSBuild noise, progress lines, and duplicate diagnostics.

        ## Installation

        ```sh
        dotnet tool install -g DotnetTokenKiller  # requires .NET 10 SDK
        ```

        ## Usage

        Drop-in replacement for `dotnet build`, `test`, `restore`, `clean`, and `format`. All arguments and flags are forwarded unchanged:

        ```sh
        dtk dotnet build MyProject.slnx
        dtk dotnet test --filter "Category=Unit"
        dtk dotnet restore
        dtk dotnet clean
        dtk dotnet format
        dtk dotnet format --verify-no-changes
        ```

        Unknown subcommands (e.g. `run`, `publish`) pass through to `dotnet` unchanged.

        ## Flags

        | Flag         | Purpose                                           |
        |--------------|---------------------------------------------------|
        | `--show-log` | Print path to full unfiltered log after a run     |
        | `-v`         | Echo the resolved command line before running     |
        | `--vv`       | Also dump raw dotnet output and elapsed time      |

        ## Key Behaviors

        - Paths are workspace-relative (`src/Foo.cs`, not absolute)
        - Build errors grouped by file; warnings grouped by diagnostic code with frequency counts
        - Exit codes preserved — CI pipelines work correctly
        - Works with xUnit, NUnit, MSTest, and Reqnroll
        - Run `dtk dotnet clean` first for a full warning report (incremental builds skip unchanged files)
        - The PreToolUse hook shells out to `python3`; on Windows (where the launcher is usually `python`, not `python3`), edit the `command` in `.claude/settings.json` if the hook doesn't fire

        ## Token Savings

        ```sh
        dtk gain               # last 30 days
        dtk gain --days 7
        dtk gain --project     # current project only
        dtk gain --json
        ```
        """;

    /// <inheritdoc/>
    public string ProviderName => "claude";

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateAsync(
        string directory,
        bool force,
        CancellationToken cancellationToken)
        => IntegrateCoreAsync(Path.Combine(directory, ".claude"), directory, HookCommand, force, cancellationToken);

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(home.ClaudeDir, home.Home, GlobalHookCommand, force, cancellationToken);

    private async Task<IntegrationResult> IntegrateCoreAsync(
        string baseDirectory,
        string reconcileDir,
        string hookCommand,
        bool force,
        CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteFileAsync(
            Path.Combine(baseDirectory, "skills", "dotnet-token-killer", "SKILL.md"),
            SkillMarkdown, context, cancellationToken).ConfigureAwait(false);

        await IntegratorHelpers.WriteHookAndSettingsAsync(
            new HookSpec(
                Path.Combine(baseDirectory, "hooks", "dotnet-to-dtk.py"),
                HookScriptTemplates.ClaudeHook,
                Path.Combine(baseDirectory, "settings.json"),
                "PreToolUse",
                "Bash",
                hookCommand),
            context, cancellationToken).ConfigureAwait(false);

        var rtkOutcome = await rtk.ReconcileAsync(reconcileDir, cancellationToken).ConfigureAwait(false);
        if (rtkOutcome.CreatedConfigPath is not null)
        {
            context.Created.Add(rtkOutcome.CreatedConfigPath);
        }

        if (rtkOutcome.UpdatedConfigPath is not null)
        {
            context.Updated.Add(rtkOutcome.UpdatedConfigPath);
        }

        context.Notes.AddRange(rtkOutcome.Notes);

        return context.ToResult();
    }
}
