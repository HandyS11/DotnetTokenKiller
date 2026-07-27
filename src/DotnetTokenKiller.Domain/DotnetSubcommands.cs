namespace DotnetTokenKiller.Domain;

/// <summary>
/// The dotnet subcommands dtk filters. This is the single source of truth: CLI routing, shell
/// completion, the keyed filter registrations, and the generated agent hook scripts all derive
/// from it, so a new subcommand cannot be half-added.
/// </summary>
/// <remarks>
/// Adding a subcommand touches every one of these, in order:
/// <list type="number">
///   <item><description>Add the <c>const</c> and an <see cref="Ordered"/> entry here.</description></item>
///   <item><description>
///   Add the filter class, its <see cref="Filters.FilterKeys"/> constant, and its
///   <c>AddKeyedTransient</c> registration in <c>ServiceCollectionExtensions.AddApplication</c>.
///   </description></item>
///   <item><description>
///   Add the <c>Dotnet*Command</c> and its registration in <c>CliConfigurator</c>.
///   </description></item>
///   <item><description>Update the two pinned literals in <c>SubcommandBindingTests</c>.</description></item>
///   <item><description>
///   Regenerate <c>.claude/hooks/dotnet-to-dtk.py</c> from <c>HookScriptTemplates.ClaudeHook</c>
///   (e.g. run <c>dtk integrate claude</c> against a scratch directory and copy the result over).
///   </description></item>
///   <item><description>
///   Update the subcommand prose in <c>src/DotnetTokenKiller.Application/Integration/</c> —
///   <c>IntegrationInstructions.cs</c> and <c>CopilotCliIntegrator.cs</c> still hardcode the list
///   in the instruction text shipped to users, and nothing guards them.
///   </description></item>
/// </list>
/// </remarks>
public static class DotnetSubcommands
{
    /// <summary>The <c>dotnet build</c> subcommand.</summary>
    public const string Build = "build";

    /// <summary>The <c>dotnet test</c> subcommand.</summary>
    public const string Test = "test";

    /// <summary>The <c>dotnet restore</c> subcommand.</summary>
    public const string Restore = "restore";

    /// <summary>The <c>dotnet clean</c> subcommand.</summary>
    public const string Clean = "clean";

    /// <summary>The <c>dotnet format</c> subcommand.</summary>
    public const string Format = "format";

    /// <summary>
    /// Canonical display order, used for CLI routing, help, and completion. Changing this order
    /// changes the order commands are listed in <c>dtk dotnet --help</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> Ordered = [Build, Test, Restore, Clean, Format];

    /// <summary>Case-insensitive membership test, used to tell a dtk-handled invocation from a passthrough.</summary>
    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(Ordered, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ordinal alphabetical order. Generated artifacts (the Python hook's subcommand tuple) use
    /// this so their bytes stay stable regardless of how <see cref="Ordered"/> is rearranged.
    /// </summary>
    public static readonly IReadOnlyList<string> Sorted = [.. Ordered.Order(StringComparer.Ordinal)];
}
