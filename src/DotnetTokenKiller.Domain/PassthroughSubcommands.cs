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
            "publish", "pack", "list", "tool", "workload", "sln", "msbuild", "ef"
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
    /// this, <c>list package</c> (worth filtering) and <c>list reference</c> (not) collapse into
    /// one row.
    /// </summary>
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
    /// which <c>dotnet publish</c>/<c>pack</c> use to prompt for private-feed credentials, so those
    /// invocations are excluded here even though their subcommand is otherwise on the allowlist —
    /// they fall back to the inherited-stdio passthrough path and record as
    /// <see cref="Tracking.RunOutcome.PassthroughUnmeasured"/> instead.
    /// </remarks>
    public static bool IsMeasurable(IReadOnlyList<string> dotnetArgs)
    {
        ArgumentNullException.ThrowIfNull(dotnetArgs);

        if (dotnetArgs.Count == 0 || !Measurable.Contains(dotnetArgs[0]))
        {
            return false;
        }

        return !dotnetArgs.Any(arg => string.Equals(arg, "--interactive", StringComparison.OrdinalIgnoreCase));
    }

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
