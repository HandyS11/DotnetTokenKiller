using DotnetTokenKiller.Domain;

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
    /// <summary>The <c>dotnet</c> driver command that dtk-handled invocations begin with.</summary>
    private const string DotnetCommand = "dotnet";

    private static readonly HashSet<string> DtkOptions =
        new(StringComparer.Ordinal)
        {
            "-v",
            "--verbose",
            "--vv",
            "--show-log",
            "-q",
            "--quiet",
            "-h",
            "--help"
        };

    /// <summary>
    /// Canonicalizes the casing of a known <c>dotnet &lt;subcommand&gt;</c> invocation so
    /// Spectre.Console's case-sensitive routing matches (e.g. <c>dtk DOTNET BUILD</c> →
    /// <c>dotnet build</c>). Unknown/passthrough invocations are returned untouched, since
    /// their subcommand is forwarded verbatim to <c>dotnet</c>.
    /// </summary>
    /// <param name="args">The raw CLI arguments.</param>
    internal static string[] Normalize(string[] args)
    {
        if (args.Length < 2 ||
            !string.Equals(args[0], DotnetCommand, StringComparison.OrdinalIgnoreCase) ||
            !DotnetSubcommands.TryMatch(args[1..], out var match))
        {
            return args;
        }

        var canonicalTokens = match.Name.Split(' ');
        var alreadyCanonical =
            string.Equals(args[0], DotnetCommand, StringComparison.Ordinal) &&
            !canonicalTokens.Where((token, i) => !string.Equals(args[1 + i], token, StringComparison.Ordinal)).Any();

        if (alreadyCanonical)
        {
            return args;
        }

        var normalized = (string[])args.Clone();
        normalized[0] = DotnetCommand;
        for (var i = 0; i < canonicalTokens.Length; i++)
        {
            normalized[1 + i] = canonicalTokens[i];
        }

        return normalized;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the invocation should bypass the Spectre app
    /// and forward directly to <c>dotnet</c>.
    /// </summary>
    /// <param name="args">The raw CLI arguments.</param>
    internal static bool IsPassthrough(string[] args)
    {
        return args.Length >= 2 &&
               string.Equals(args[0], DotnetCommand, StringComparison.OrdinalIgnoreCase) &&
               !DotnetSubcommands.TryMatch(args[1..], out _);
    }

    /// <summary>
    /// Inserts <c>--</c> before dotnet-specific args so Spectre.Console forwards them
    /// as <c>Remaining.Raw</c> without treating them as its own options.
    /// DTK flags (<c>-v</c>, <c>--verbose</c>, <c>--vv</c>, <c>--show-log</c>, <c>-q</c>, <c>--quiet</c>)
    /// are partitioned ahead of the inserted <c>--</c>. A user's own <c>--</c> and everything after it
    /// is forwarded verbatim: Spectre consumes only the first (inserted) separator, so the user's
    /// separator survives into <c>Remaining.Raw</c> (e.g. <c>dotnet test -- RunConfiguration.X=1</c>).
    /// Returns the same array unchanged when no modification is needed.
    /// </summary>
    /// <param name="args">The raw CLI arguments.</param>
    internal static string[] InsertSeparator(string[] args)
    {
        if (args.Length <= 2 ||
            !string.Equals(args[0], DotnetCommand, StringComparison.OrdinalIgnoreCase) ||
            !DotnetSubcommands.TryMatch(args[1..], out var match))
        {
            return args;
        }

        var firstArgIndex = 1 + match.TokenCount;
        if (args.Length <= firstArgIndex)
        {
            return args;
        }

        var dtkFlags = new List<string>();
        var dotnetArgs = new List<string>();
        var sawUserSeparator = false;
        for (var i = firstArgIndex; i < args.Length; i++)
        {
            // Once the user's own "--" is seen, it and everything after it is forwarded
            // verbatim — never reinterpreted as a dtk flag.
            if (!sawUserSeparator && string.Equals(args[i], "--", StringComparison.Ordinal))
            {
                sawUserSeparator = true;
            }

            if (!sawUserSeparator && DtkOptions.Contains(args[i]))
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

        var updated = new List<string>(args.Length + 1);
        updated.AddRange(args[..firstArgIndex]);
        updated.AddRange(dtkFlags);
        updated.Add("--");
        updated.AddRange(dotnetArgs);
        return [.. updated];
    }
}
