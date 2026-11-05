namespace DotnetTokenKiller.Domain;

/// <summary>
/// Classifies <c>dotnet</c> invocations that dtk does not filter, for coverage tracking.
/// </summary>
/// <remarks>
/// Every value this type returns comes from a hardcoded allowlist. No fragment of the user's
/// argument list is ever passed through, because those arguments routinely contain file paths,
/// package names, and connection strings, and this value is written to the tracking database
/// and displayed in reports.
/// </remarks>
public static class PassthroughSubcommands
{
    /// <summary>The command name recorded when the invocation matches no known verb or option.</summary>
    public const string Unknown = "(other)";

    /// <summary>
    /// Subcommands whose output is batch rather than interactive, so capturing it to measure its
    /// size does not change behaviour the user depends on.
    /// </summary>
    public static readonly IReadOnlySet<string> Measurable =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "list", "tool", "workload", "sln", "msbuild", "ef"
        };

    /// <summary>
    /// Filtered subcommands that are passed through all the same when run with <c>--interactive</c>,
    /// which they use to prompt for private-feed credentials.
    /// </summary>
    private static readonly HashSet<string> PassedThroughWhenInteractive =
        new(StringComparer.OrdinalIgnoreCase)
        {
            DotnetSubcommands.Publish, DotnetSubcommands.Pack, DotnetSubcommands.ToolRestore
        };

    /// <summary>
    /// The options with which a tool in <see cref="DotnetSubcommands.ToolBacked"/> prints its help: ef's, Stryker's
    /// and jb's <c>-h</c>, <c>--help</c> and <c>-?</c>.
    /// </summary>
    private static readonly HashSet<string> ToolHelpOptions = new(StringComparer.Ordinal) { "-h", "--help", "-?" };

    /// <summary>
    /// Filtered subcommands that hand their arguments to MSBuild, which understands the
    /// <see cref="MsBuildQuerySwitches"/>.
    /// </summary>
    private static readonly HashSet<string> MsBuildQueryVerbs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            DotnetSubcommands.Build, DotnetSubcommands.Test, DotnetSubcommands.Restore,
            DotnetSubcommands.Clean, DotnetSubcommands.Publish, DotnetSubcommands.Pack
        };

    /// <summary>
    /// MSBuild switches that print a property, item or target result (plain text or JSON) instead of a build
    /// log, spelled <c>-name</c>, <c>--name</c> or <c>/name</c> and ignoring case.
    /// </summary>
    private static readonly string[] MsBuildQuerySwitches = ["getProperty", "getItem", "getTargetResult"];

    /// <summary>The <c>dotnet list package</c> option that selects JSON output, as <c>--format json</c> or <c>--format=json</c>.</summary>
    private const string FormatOption = "--format";

    /// <summary>The first token of a <c>dotnet ef</c> invocation.</summary>
    private const string EfVerb = "ef";

    /// <summary>The <c>dotnet ef</c> option that prints a JSON document instead of a log.</summary>
    private const string EfJsonOption = "--json";

    /// <summary>The separator after which no argument is the tool's; ef hands the rest to the application.</summary>
    private const string EndOfOptions = "--";

    /// <summary>The <c>dotnet ef migrations script</c> options that write the SQL to a file, not stdout.</summary>
    private static readonly string[] EfOutputOptions = ["-o", "--output"];

    /// <summary>
    /// <c>dotnet ef</c> invocations, keyed by their (subcommand, verb) token pair, that block on an
    /// interactive confirmation read from standard input unless one of the listed flags is present.
    /// </summary>
    /// <remarks>
    /// Sourced from the EF Core tools source (dotnet/efcore, <c>main</c> branch, checked 2026-09-23):
    /// a full-repository search for <c>Console.ReadLine</c> finds exactly one interactive read in the
    /// whole tool, in <c>src/ef/Commands/DatabaseDropCommand.cs</c>
    /// (https://github.com/dotnet/efcore/blob/main/src/ef/Commands/DatabaseDropCommand.cs) — it is
    /// skipped when <c>-f|--force</c> is given (also when <c>--dry-run</c> is given, but that flag
    /// does not drop the database, so it is not listed here as a substitute for <c>--force</c>). The
    /// EF Core CLI reference documents the same contract: "<c>--force</c> (<c>-f</c>) - Don't confirm
    /// the deletion." (https://learn.microsoft.com/en-us/ef/core/cli/dotnet#dotnet-ef-database-drop).
    /// <c>dotnet ef migrations remove</c> does <em>not</em> prompt, despite also taking a
    /// <c>--force</c> flag: <c>MigrationsScaffolder.RemoveMigration</c>
    /// (src/EFCore.Design/Migrations/Design/MigrationsScaffolder.cs) throws an <c>OperationException</c>
    /// instead of reading input when the last migration was already applied to the database and
    /// <c>--force</c> is absent — the CLI reference describes its <c>--force</c> as "revert the latest
    /// migration", not a confirmation. It is deliberately not listed here.
    /// </remarks>
    private static readonly Dictionary<(string Subcommand, string Verb), IReadOnlySet<string>> PromptingEfInvocations =
        new()
        {
            [("database", "drop")] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--force", "-f" }
        };

    /// <summary>
    /// Real <c>dotnet</c> subcommands. A first token outside this set is not a subcommand — most
    /// importantly it may be an assembly path, as in <c>dotnet ./bin/App.dll</c>.
    /// </summary>
    private static readonly HashSet<string> KnownVerbs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "add", "build", "build-server", "clean", "dev-certs", "ef", "format", "fsi", "help",
            "list", "msbuild", "new", "nuget", "pack", "package", "publish", "reference", "remove",
            "restore", "run", "sdk", "sln", "solution", "store", "test", "tool", "user-jwts",
            "user-secrets", "vstest", "watch", "workload"
        };

    /// <summary>Top-level <c>dotnet</c> options that are worth recording under their own name.</summary>
    private static readonly HashSet<string> KnownOptions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "--info", "--version", "--list-sdks", "--list-runtimes", "--help", "-h", "--diagnostics"
        };

    /// <summary>
    /// The second token, per subcommand, that meaningfully changes what the command does. Without
    /// this, invocations that differ only in that token — <c>list reference</c> and <c>list package</c>
    /// — collapse into one indistinguishable coverage row.
    /// </summary>
    /// <remarks>
    /// <c>"package"</c> under <c>list</c> is retained on purpose even though <c>dotnet list package</c>
    /// is now a filtered subcommand that no longer reaches this code. Coverage rows recorded before it
    /// was filtered are keyed <c>"list package"</c>, and dropping the token here would leave that key
    /// as something the allowlist no longer admits — so those rows would stop being interpretable as
    /// a name this type can produce. The entry is therefore correct but currently unreachable:
    /// <c>PassthroughRunUseCase</c> is the only caller, and an invocation whose first two tokens are
    /// <c>list package</c> is routed to the filtered command by
    /// <c>DotnetSubcommands.TryMatch</c> before it can get here. Uncovered spellings such as
    /// <c>dotnet list &lt;SOLUTION&gt; package</c> do reach this code, but record as plain
    /// <c>"list"</c>, because only <c>dotnetArgs[1]</c> is consulted.
    /// </remarks>
    private static readonly Dictionary<string, IReadOnlySet<string>> QualifyingVerbs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["list"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "package", "reference" },
            ["ef"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "migrations", "database", "dbcontext" },
            ["tool"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "list", "restore", "install", "update" },
            ["sln"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "list", "add", "remove" },
            ["workload"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "list", "search" },
            ["nuget"] = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "locals", "push", "verify" }
        };

    /// <summary>Reduces a passthrough invocation to a safe, low-cardinality command name.</summary>
    /// <param name="dotnetArgs">
    /// The arguments passed to <c>dotnet</c>, starting at the subcommand (that is,
    /// <c>args[1..]</c> at the passthrough branch in <c>Program.cs</c>).
    /// </param>
    /// <returns>
    /// A name drawn entirely from this type's allowlists, such as <c>"list package"</c>,
    /// <c>"publish"</c>, <c>"--info"</c>, or <see cref="Unknown"/>.
    /// </returns>
    public static string CommandName(IReadOnlyList<string> dotnetArgs)
    {
        ArgumentNullException.ThrowIfNull(dotnetArgs);

        if (dotnetArgs.Count == 0)
        {
            return Unknown;
        }

        var head = dotnetArgs[0];

        if (KnownOptions.Contains(head))
        {
            return Canonical(KnownOptions, head);
        }

        if (!KnownVerbs.Contains(head))
        {
            return Unknown;
        }

        var verb = Canonical(KnownVerbs, head);

        if (dotnetArgs.Count < 2 ||
            !QualifyingVerbs.TryGetValue(verb, out var allowed) ||
            !allowed.Contains(dotnetArgs[1]))
        {
            return verb;
        }

        return $"{verb} {Canonical(allowed, dotnetArgs[1])}";
    }

    /// <summary>
    /// Returns <see langword="true"/> when this invocation's output can be captured and measured
    /// without changing behaviour the user depends on.
    /// </summary>
    /// <param name="dotnetArgs">
    /// The arguments passed to <c>dotnet</c>, starting at the subcommand.
    /// </param>
    /// <remarks>
    /// <c>RunStreamedAsync</c> (the path a measurable command takes) closes the child's stdin so a
    /// child reading stdin sees EOF rather than hanging. That is wrong for <c>--interactive</c>,
    /// which dotnet commands use to prompt for private-feed credentials, so those invocations are
    /// excluded here even though their subcommand is otherwise on the allowlist — they fall back to
    /// the inherited-stdio passthrough path and record as
    /// <see cref="Tracking.RunOutcome.PassthroughUnmeasured"/> instead. The same is true of the
    /// <c>dotnet ef</c> invocations in <see cref="PromptingEfInvocations"/>: closing stdin would make
    /// their confirmation read see EOF immediately, so they are excluded unless the flag that
    /// suppresses the prompt is present. A machine-readable run (<see cref="IsMachineReadableRun"/>) is
    /// excluded too: captured output is re-split into lines, and its reader wants dotnet's bytes.
    /// </remarks>
    public static bool IsMeasurable(IReadOnlyList<string> dotnetArgs)
    {
        ArgumentNullException.ThrowIfNull(dotnetArgs);

        if (dotnetArgs.Count == 0 || !Measurable.Contains(dotnetArgs[0]))
        {
            return false;
        }

        return !HasInteractiveFlag(dotnetArgs)
               && !PromptsForConfirmation(dotnetArgs)
               && !IsMachineReadableRun(dotnetArgs);
    }

    /// <summary>
    /// Returns <see langword="true"/> when this invocation reads a confirmation from standard input: it matches one of
    /// the <see cref="PromptingEfInvocations"/> entries (<c>dotnet ef database drop</c>) and none of that entry's
    /// non-interactive flags (<c>-f</c>, <c>--force</c>) are present.
    /// </summary>
    /// <param name="dotnetArgs">The arguments passed to <c>dotnet</c>, starting at the subcommand.</param>
    public static bool PromptsForConfirmation(IReadOnlyList<string> dotnetArgs)
    {
        ArgumentNullException.ThrowIfNull(dotnetArgs);

        if (dotnetArgs.Count < 3 || !string.Equals(dotnetArgs[0], EfVerb, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var (key, nonInteractiveFlags) in PromptingEfInvocations)
        {
            if (string.Equals(dotnetArgs[1], key.Subcommand, StringComparison.OrdinalIgnoreCase)
                && string.Equals(dotnetArgs[2], key.Verb, StringComparison.OrdinalIgnoreCase))
            {
                return !dotnetArgs.Any(arg => nonInteractiveFlags.Contains(arg));
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when this invocation of a filtered subcommand must be passed
    /// through rather than filtered because it may prompt: <c>dotnet publish</c>, <c>dotnet pack</c> or
    /// <c>dotnet tool restore</c> run with <c>--interactive</c>, or <c>dotnet ef database drop</c> without
    /// <c>-f</c>/<c>--force</c> (see <see cref="PromptsForConfirmation"/>).
    /// </summary>
    /// <param name="dotnetArgs">
    /// The arguments passed to <c>dotnet</c>, starting at the subcommand.
    /// </param>
    /// <remarks>
    /// A filtered run captures the child's output until it exits and closes its stdin, so a
    /// credential provider's prompt (or the device code it prints) would never reach the user, and ef's
    /// confirmation read would see EOF at once. These were passed through, with the terminal attached, before dtk
    /// filtered them, and these runs keep doing so; they record as
    /// <see cref="Tracking.RunOutcome.PassthroughUnmeasured"/> via <see cref="IsMeasurable"/>.
    /// </remarks>
    public static bool IsInteractiveFilteredRun(IReadOnlyList<string> dotnetArgs)
    {
        ArgumentNullException.ThrowIfNull(dotnetArgs);

        return DotnetSubcommands.TryMatch(dotnetArgs, out var match)
               && ((PassedThroughWhenInteractive.Contains(match.Name) && HasInteractiveFlag(dotnetArgs))
                   || PromptsForConfirmation(dotnetArgs));
    }

    /// <summary>
    /// Returns <see langword="true"/> when this invocation of a subcommand in
    /// <see cref="DotnetSubcommands.ToolBacked"/> asks the tool for its help (<c>-h</c>, <c>--help</c> or <c>-?</c>
    /// before any <c>--</c>), which a filter would replace with a status line: it is passed through, unfiltered.
    /// </summary>
    /// <param name="dotnetArgs">The arguments passed to <c>dotnet</c>, starting at the subcommand.</param>
    /// <remarks>
    /// A dotnet verb's help is not the tool's: on <c>dtk dotnet test --help</c> or
    /// <c>dtk dotnet tool restore --help</c> dtk prints its own help for the command.
    /// </remarks>
    public static bool IsToolHelpRun(IReadOnlyList<string> dotnetArgs)
    {
        ArgumentNullException.ThrowIfNull(dotnetArgs);

        return DotnetSubcommands.TryMatch(dotnetArgs, out var match)
               && DotnetSubcommands.ToolBacked.Contains(match.Name)
               && OptionsBeforeSeparator(dotnetArgs, match.TokenCount).Any(ToolHelpOptions.Contains);
    }

    /// <summary>
    /// Returns <see langword="true"/> when this invocation prints output for a program to parse, which a
    /// filter would corrupt or replace: <c>dotnet list package --format json</c>, an MSBuild query
    /// (<c>-getProperty</c>, <c>-getItem</c>, <c>-getTargetResult</c>) on <c>build</c>, <c>test</c>,
    /// <c>restore</c>, <c>clean</c>, <c>publish</c> or <c>pack</c>, any <c>dotnet ef</c> command with <c>--json</c>,
    /// or <c>dotnet ef migrations script</c> without <c>-o</c>/<c>--output</c>, which prints the SQL itself.
    /// </summary>
    /// <param name="dotnetArgs">
    /// The arguments passed to <c>dotnet</c>, starting at the subcommand.
    /// </param>
    /// <remarks>
    /// The list-package filter would append a <c>⚠</c> line to the JSON document, and a query prints the
    /// requested value and no build log, which a build filter would replace with a status line. These runs are
    /// passed through with the terminal attached, like <see cref="IsInteractiveFilteredRun"/>'s, so the
    /// bytes a reader sees are dotnet's own; they record as
    /// <see cref="Tracking.RunOutcome.PassthroughUnmeasured"/> via <see cref="IsMeasurable"/>.
    /// </remarks>
    public static bool IsMachineReadableRun(IReadOnlyList<string> dotnetArgs)
    {
        ArgumentNullException.ThrowIfNull(dotnetArgs);

        if (dotnetArgs.Count == 0)
        {
            return false;
        }

        if (string.Equals(dotnetArgs[0], "list", StringComparison.OrdinalIgnoreCase))
        {
            return dotnetArgs.Count > 1
                   && string.Equals(dotnetArgs[1], "package", StringComparison.OrdinalIgnoreCase)
                   && RequestsJsonFormat(dotnetArgs);
        }

        if (string.Equals(dotnetArgs[0], EfVerb, StringComparison.OrdinalIgnoreCase))
        {
            return IsMachineReadableEfRun(dotnetArgs);
        }

        return MsBuildQueryVerbs.Contains(dotnetArgs[0]) && HasMsBuildQuery(dotnetArgs);
    }

    /// <summary>
    /// Whether a <c>dotnet ef</c> invocation prints a document: <c>--json</c>, or <c>migrations script</c> with no
    /// output file. Only ef's own arguments count, those before <c>--</c>; ef hands the rest to the application.
    /// </summary>
    /// <param name="dotnetArgs">The arguments passed to <c>dotnet</c>, starting at <c>ef</c>.</param>
    private static bool IsMachineReadableEfRun(IReadOnlyList<string> dotnetArgs)
    {
        var efArgs = OptionsBeforeSeparator(dotnetArgs, 1).ToList();
        if (efArgs.Exists(arg => string.Equals(arg, EfJsonOption, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return efArgs.Count >= 2
               && string.Equals(efArgs[0], "migrations", StringComparison.OrdinalIgnoreCase)
               && string.Equals(efArgs[1], "script", StringComparison.OrdinalIgnoreCase)
               && !efArgs.Exists(IsEfOutputOption);
    }

    /// <summary>
    /// Whether <paramref name="arg"/> is <c>-o</c> or <c>--output</c>, alone or with its value joined by <c>=</c> or
    /// <c>:</c>, as ef's parser accepts.
    /// </summary>
    /// <param name="arg">One argument.</param>
    private static bool IsEfOutputOption(string arg) =>
        EfOutputOptions.Any(option =>
            arg.StartsWith(option, StringComparison.Ordinal)
            && (arg.Length == option.Length || arg[option.Length] is '=' or ':'));

    /// <summary>The arguments from <paramref name="start"/> up to the first <c>--</c>, which is not included.</summary>
    /// <param name="dotnetArgs">The arguments passed to <c>dotnet</c>, starting at the subcommand.</param>
    /// <param name="start">The index of the first argument to return.</param>
    private static IEnumerable<string> OptionsBeforeSeparator(IReadOnlyList<string> dotnetArgs, int start) =>
        dotnetArgs.Skip(start).TakeWhile(arg => !string.Equals(arg, EndOfOptions, StringComparison.Ordinal));

    /// <summary>Whether <c>--format json</c>, <c>--format=json</c> or <c>--format:json</c> follows the subcommand.</summary>
    /// <param name="dotnetArgs">The arguments passed to <c>dotnet</c>, starting at the subcommand.</param>
    private static bool RequestsJsonFormat(IReadOnlyList<string> dotnetArgs)
    {
        for (var i = 1; i < dotnetArgs.Count; i++)
        {
            if (FormatValue(dotnetArgs, i).Equals("json", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The value the <c>--format</c> option at <paramref name="index"/> sets, or empty when it is another argument.</summary>
    /// <param name="dotnetArgs">The arguments passed to <c>dotnet</c>.</param>
    /// <param name="index">The index of the argument to read.</param>
    private static ReadOnlySpan<char> FormatValue(IReadOnlyList<string> dotnetArgs, int index)
    {
        var arg = dotnetArgs[index];
        if (!arg.StartsWith(FormatOption, StringComparison.OrdinalIgnoreCase))
        {
            return default;
        }

        if (arg.Length == FormatOption.Length)
        {
            return index + 1 < dotnetArgs.Count ? dotnetArgs[index + 1].AsSpan() : default;
        }

        return arg[FormatOption.Length] is ':' or '=' ? arg.AsSpan(FormatOption.Length + 1) : default;
    }

    /// <summary>Whether any argument after the subcommand is one of the <see cref="MsBuildQuerySwitches"/>.</summary>
    /// <param name="dotnetArgs">The arguments passed to <c>dotnet</c>, starting at the subcommand.</param>
    private static bool HasMsBuildQuery(IReadOnlyList<string> dotnetArgs)
    {
        for (var i = 1; i < dotnetArgs.Count; i++)
        {
            if (IsMsBuildQuerySwitch(dotnetArgs[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="arg"/> is a query switch: a <c>-</c>, <c>--</c> or <c>/</c> prefix, the switch
    /// name, then nothing or a <c>:</c> or <c>=</c> and its value.
    /// </summary>
    /// <param name="arg">One argument.</param>
    private static bool IsMsBuildQuerySwitch(string arg)
    {
        var name = arg.AsSpan();
        if (name.Length < 2 || name[0] is not ('-' or '/'))
        {
            return false;
        }

        name = name[(name[0] == '-' && name[1] == '-' ? 2 : 1)..];
        var valueStart = name.IndexOfAny(':', '=');
        if (valueStart >= 0)
        {
            name = name[..valueStart];
        }

        foreach (var query in MsBuildQuerySwitches)
        {
            if (name.Equals(query, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasInteractiveFlag(IReadOnlyList<string> dotnetArgs) =>
        dotnetArgs.Any(arg => string.Equals(arg, "--interactive", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns the allowlist's own spelling of <paramref name="value"/>, so a name recorded from
    /// <c>DOTNET LIST PACKAGE</c> is identical to one recorded from <c>dotnet list package</c> and
    /// the two do not become separate rows.
    /// </summary>
    /// <param name="allowlist">The set that matched, searched case-insensitively.</param>
    /// <param name="value">The user-supplied token that matched.</param>
    private static string Canonical(IReadOnlySet<string> allowlist, string value)
    {
        return allowlist.FirstOrDefault(candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase))
               ?? value;
    }
}
