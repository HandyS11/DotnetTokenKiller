namespace DotnetTokenKiller.Cli;

/// <summary>
/// Preprocesses CLI arguments before they reach the Spectre.Console command app.
/// </summary>
/// <remarks>
/// Handles two cases:
/// <list type="bullet">
///   <item><description>Passthrough: <c>dotnet &lt;subcommand not handled by dtk&gt;</c> should bypass the app entirely.</description></item>
///   <item><description>Separator insertion: <c>dtk dotnet build &lt;args&gt;</c> needs <c>--</c> inserted so
///         Spectre forwards dotnet-specific args via <c>Remaining.Raw</c>.</description></item>
/// </list>
/// </remarks>
internal static class ArgumentPreprocessor
{
    /// <summary>Subcommand name for <c>dotnet build</c>. Used in both the command registration and passthrough detection.</summary>
    internal const string BuildSubcommand = "build";

    /// <summary>Subcommand name for <c>dotnet test</c>. Used in both the command registration and passthrough detection.</summary>
    internal const string TestSubcommand = "test";

    /// <summary>Subcommand name for <c>dotnet restore</c>. Used in both the command registration and passthrough detection.</summary>
    internal const string RestoreSubcommand = "restore";

    /// <summary>Subcommand name for <c>dotnet clean</c>. Used in both the command registration and passthrough detection.</summary>
    internal const string CleanSubcommand = "clean";

    /// <summary>
    /// Dotnet subcommands handled by dtk. This set is the single source of truth — the command
    /// registrations in Program.cs reference these constants to stay in sync automatically.
    /// </summary>
    internal static readonly IReadOnlySet<string> KnownSubcommands =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            BuildSubcommand,
            TestSubcommand,
            RestoreSubcommand,
            CleanSubcommand
        };

    private static readonly HashSet<string> DtkOptions =
        new(StringComparer.Ordinal)
        {
            "-v",
            "--verbose",
            "--show-log"
        };

    /// <summary>
    /// Returns <see langword="true"/> when the invocation should bypass the Spectre app
    /// and forward directly to <c>dotnet</c>.
    /// </summary>
    /// <param name="args">The raw CLI arguments.</param>
    internal static bool IsPassthrough(string[] args)
    {
        return args.Length >= 2 &&
               string.Equals(args[0], "dotnet", StringComparison.OrdinalIgnoreCase) &&
               !KnownSubcommands.Contains(args[1]);
    }

    /// <summary>
    /// Inserts <c>--</c> before dotnet-specific args so Spectre.Console forwards them
    /// as <c>Remaining.Raw</c> without treating them as its own options.
    /// DTK flags (<c>-v</c>, <c>--verbose</c>, <c>--show-log</c>) are partitioned before <c>--</c>.
    /// Returns the same array unchanged when no modification is needed.
    /// </summary>
    /// <param name="args">The raw CLI arguments.</param>
    internal static string[] InsertSeparator(string[] args)
    {
        if (args.Length <= 2 ||
            !string.Equals(args[0], "dotnet", StringComparison.OrdinalIgnoreCase) ||
            !KnownSubcommands.Contains(args[1]) ||
            args.Contains("--"))
        {
            return args;
        }

        var dtkFlags = new List<string>();
        var dotnetArgs = new List<string>();
        for (var i = 2; i < args.Length; i++)
        {
            if (DtkOptions.Contains(args[i]))
            {
                dtkFlags.Add(args[i]);
            }
            else
            {
                dotnetArgs.Add(args[i]);
            }
        }

        if (dotnetArgs.Count == 0)
        {
            return args;
        }

        var updated = new List<string>(args.Length + 1)
        {
            args[0],
            args[1]
        };
        updated.AddRange(dtkFlags);
        updated.Add("--");
        updated.AddRange(dotnetArgs);
        return [.. updated];
    }
}
