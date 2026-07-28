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
///   <item><description>
///   Check <c>CompletionCommand.CompletionCandidates</c> — a multi-token subcommand contributes only
///   its first token, and completing the remaining tokens is not implemented.
///   </description></item>
///   <item><description>
///   Check <c>ClaudeCodeIntegrator.SkillMarkdown</c>. Its frontmatter <c>description:</c> is Claude
///   Code's <em>skill-trigger</em> text — a subcommand missing from it means the installed skill
///   never surfaces for that intent, which nothing inside dtk can observe. It and the skill's
///   example block are both derived, and pinned by <c>SubcommandBindingTests</c>.
///   </description></item>
///   <item><description>
///   Update the pinned literals in <c>SubcommandBindingTests</c>, <c>DotnetSubcommandsTests</c>,
///   <c>AiderIntegratorTests</c>, and <c>GitHubCopilotIntegratorTests</c> — the last two pin fully
///   rendered instruction text, not just the list — and accept the CLI help snapshots.
///   </description></item>
///   <item><description>
///   Regenerate <c>.claude/hooks/dotnet-to-dtk.py</c> from <c>HookScriptTemplates.ClaudeHook</c>
///   (e.g. run <c>dtk integrate claude</c> against a scratch directory and copy the result over).
///   </description></item>
///   <item><description>
///   Add an example line to <c>IntegrationInstructions.UsageBody</c>. The prose lists themselves are
///   generated from <see cref="Ordered"/> and guarded by
///   <c>SubcommandBindingTests.IntegrationProse_ListsEveryCanonicalSubcommand</c>, so they need no
///   hand-editing — but that test's pinned literals do.
///   </description></item>
///   <item><description>
///   Check <c>PipeCommand</c>'s "No filter for: …" error line. It renders <see cref="Ordered"/> joined
///   with ", " on one line; a seventh entry can push that line past Spectre's 80-column
///   non-interactive wrap width and break the line-count assertion in
///   <c>PipeIntegrationTests.Pipe_UnknownSubcommand_FailsWithKnownListAsync</c>.
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

    /// <summary>The <c>dotnet list package</c> subcommand.</summary>
    /// <remarks>
    /// Two tokens, unlike every other entry.
    /// <see cref="TryMatch(IReadOnlyList{string}, out SubcommandMatch)"/> exists for this: matching
    /// only <c>args[1]</c> would capture <c>dotnet list reference</c>, which dtk must forward
    /// untouched.
    /// </remarks>
    public const string ListPackage = "list package";

    /// <summary>
    /// Canonical display order, used for CLI routing, help, and completion. Changing this order
    /// changes the order commands are listed in <c>dtk dotnet --help</c>.
    /// </summary>
    public static readonly IReadOnlyList<string> Ordered = [Build, Test, Restore, Clean, Format, ListPackage];

    /// <summary>
    /// The canonical names as a case-insensitive set, for asserting membership and binding other
    /// restatements of the list back to it.
    /// </summary>
    /// <remarks>
    /// Not usable for routing an argument list: a multi-token name such as <c>list package</c> is one
    /// entry here, so <c>All.Contains(args[1])</c> would never match it. Use
    /// <see cref="TryMatch(IReadOnlyList{string}, out SubcommandMatch)"/> for that.
    /// </remarks>
    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(Ordered, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ordinal alphabetical order. Generated artifacts (the Python hook's subcommand tuple) use
    /// this so their bytes stay stable regardless of how <see cref="Ordered"/> is rearranged.
    /// </summary>
    public static readonly IReadOnlyList<string> Sorted = [.. Ordered.Order(StringComparer.Ordinal)];

    /// <summary>
    /// Candidate token sequences, longest first. Precomputed for the public
    /// <see cref="TryMatch(IReadOnlyList{string}, out SubcommandMatch)"/> overload so routing every
    /// invocation does not re-split the canonical list.
    /// </summary>
    private static readonly IReadOnlyList<string[]> OrderedMatchers = BuildMatchers(Ordered);

    /// <summary>
    /// Matches the leading tokens of <paramref name="args"/> against the canonical subcommands.
    /// </summary>
    /// <param name="args">Arguments starting at the subcommand, e.g. <c>["list", "package", "--outdated"]</c>.</param>
    /// <param name="match">The canonical name and consumed token count, or <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when a canonical subcommand matched.</returns>
    public static bool TryMatch(IReadOnlyList<string> args, out SubcommandMatch match) =>
        TryMatchCore(args, OrderedMatchers, out match);

    /// <summary>
    /// Matches against an explicit candidate list. Exists so multi-token matching is testable
    /// independently of whichever subcommands happen to be canonical today.
    /// </summary>
    /// <param name="args">Arguments starting at the subcommand.</param>
    /// <param name="candidateNames">Canonical names to match against, space-separated for multi-token.</param>
    /// <param name="match">The canonical name and consumed token count, or <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when a candidate matched.</returns>
    internal static bool TryMatch(
        IReadOnlyList<string> args,
        IReadOnlyList<string> candidateNames,
        out SubcommandMatch match)
    {
        ArgumentNullException.ThrowIfNull(candidateNames);
        return TryMatchCore(args, BuildMatchers(candidateNames), out match);
    }

    private static IReadOnlyList<string[]> BuildMatchers(IReadOnlyList<string> names) =>
        [.. names.Select(name => name.Split(' ')).OrderByDescending(tokens => tokens.Length)];

    private static bool TryMatchCore(
        IReadOnlyList<string> args,
        IReadOnlyList<string[]> matchers,
        out SubcommandMatch match)
    {
        ArgumentNullException.ThrowIfNull(args);

        var tokens = matchers.FirstOrDefault(candidate => StartsWith(args, candidate));
        if (tokens is null)
        {
            match = default;
            return false;
        }

        match = new SubcommandMatch(string.Join(' ', tokens), tokens.Length);
        return true;
    }

    private static bool StartsWith(IReadOnlyList<string> args, string[] tokens) =>
        args.Count >= tokens.Length &&
        !tokens.Where((token, i) => !string.Equals(args[i], token, StringComparison.OrdinalIgnoreCase)).Any();
}
