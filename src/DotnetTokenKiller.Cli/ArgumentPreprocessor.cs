using System.Globalization;
using DotnetTokenKiller.Domain;
using DotnetTokenKiller.Domain.Execution;

namespace DotnetTokenKiller.Cli;

/// <summary>
/// Preprocesses CLI arguments before they reach the Spectre.Console command app.
/// </summary>
/// <remarks>
/// Handles these cases:
/// <list type="bullet">
///   <item><description>Shortcut: <c>dtk test</c>, <c>dtk build</c>, <c>dtk list package</c> and the other filtered
///         subcommands are read as <c>dtk dotnet …</c> (see <see cref="Normalize"/>).</description></item>
///   <item><description>Passthrough: <c>dotnet &lt;subcommand not handled by dtk&gt;</c> should bypass the app entirely,
///         as does a filtered one whose output is meant for another program (<c>list package --format json</c>,
///         <c>build -getProperty:Version</c>).</description></item>
///   <item><description>Raw output: under <c>DTK_RAW=1</c> any <c>dotnet</c> invocation bypasses it too, without
///         dtk's own options.</description></item>
///   <item><description>Separator insertion: <c>dtk dotnet build &lt;args&gt;</c> needs <c>--</c> inserted so
///         Spectre forwards dotnet-specific args via <c>Remaining.Raw</c>.</description></item>
///   <item><description>Usage errors and refusals: a value option without its value, a test option on another
///         subcommand, and an option a passed-through run cannot honour are reported before anything runs (see
///         <see cref="MissingOptionValue"/>, <see cref="MisplacedTestOption"/> and
///         <see cref="OptionRefusedOnPassthrough"/>).</description></item>
/// </list>
/// dtk reads its options only on the subcommands it filters, and only before the user's own <c>--</c>: any other
/// subcommand (<c>tool run</c>, <c>run</c>, <c>watch</c>, …) gets its arguments verbatim, so
/// <c>dotnet tool run x --tee y</c> hands <c>--tee y</c> to <c>x</c>. On a subcommand that runs a tool
/// (<see cref="DotnetSubcommands.ToolBacked"/>: <c>ef</c>, <c>stryker</c>, <c>jb cleanupcode</c>) dtk honours only its
/// long, dtk-only options, <c>--vv</c>, <c>--show-log</c>, <c>--tee</c> and <c>--max-lines</c>: the tool owns every
/// short option and <c>--verbose</c>, <c>--quiet</c> and <c>--help</c>, and prints its own help. The test options stay
/// dtk's there too, so they are its usage error, as on any subcommand but <c>dotnet test</c>, and never reach the tool.
/// </remarks>
internal static class ArgumentPreprocessor
{
    /// <summary>The <c>dotnet</c> driver command that dtk-handled invocations begin with.</summary>
    private const string DotnetCommand = "dotnet";

    /// <summary>
    /// The value option whose value is a number: only an integer in the next token is its value, so a project, an
    /// MSBuild query or another token after it stays dotnet's and the number is reported missing.
    /// </summary>
    private const string MaxLinesOption = "--max-lines";

    /// <summary>dtk's output options, which dotnet does not understand.</summary>
    private static readonly HashSet<string> OutputOptions =
        new(StringComparer.Ordinal)
        {
            "-v",
            "--verbose",
            "--vv",
            "--show-log",
            "-q",
            "--quiet"
        };

    /// <summary>
    /// dtk's options that take a value, given as the next token (<c>--tee out.log</c>) or joined to the name by
    /// <c>=</c> or <c>:</c> (<c>--tee=out.log</c>, <c>--max-lines:5</c>), all of which Spectre reads.
    /// </summary>
    private static readonly HashSet<string> ValueOptions =
        new(StringComparer.Ordinal)
        {
            "--tee",
            MaxLinesOption
        };

    /// <summary>dtk's options that apply only to <c>dotnet test</c>.</summary>
    private static readonly HashSet<string> TestOnlyOptions =
        new(StringComparer.Ordinal)
        {
            "--names",
            "--show-output",
            "--fail-on-empty"
        };

    /// <summary>Help, which Spectre answers on a filtered run and dotnet on a passed-through one.</summary>
    private static readonly HashSet<string> HelpOptions =
        new(StringComparer.Ordinal)
        {
            "-h",
            "--help"
        };

    /// <summary>Every flag Spectre must see before the inserted <c>--</c>: the output and test options, and help.</summary>
    private static readonly HashSet<string> DtkOptions =
        new(OutputOptions.Concat(TestOnlyOptions).Concat(HelpOptions), StringComparer.Ordinal);

    /// <summary>
    /// The flags dtk reads on a subcommand whose tool owns the others (<see cref="DotnetSubcommands.ToolBacked"/>): its
    /// long, dtk-only output flags, and the test options, which it reports as misplaced rather than hand to the tool.
    /// Its <see cref="ValueOptions"/> are dtk's there too.
    /// </summary>
    private static readonly HashSet<string> ToolBackedOptions =
        new(new[] { "--vv", "--show-log" }.Concat(TestOnlyOptions), StringComparer.Ordinal);

    /// <summary>
    /// The options a filtered subcommand passed through anyway cannot honour, because one promises a file and the
    /// other changes the verdict: dtk refuses the run rather than run it without them.
    /// </summary>
    private static readonly HashSet<string> RefusedOnPassthrough =
        new(StringComparer.Ordinal)
        {
            "--tee",
            "--fail-on-empty"
        };

    /// <summary>The display options a filtered subcommand passed through anyway drops with a warning.</summary>
    private static readonly HashSet<string> DroppedOnPassthrough =
        new(StringComparer.Ordinal)
        {
            "--max-lines",
            "--names",
            "--show-output"
        };

    /// <summary>The values dotnet accepts for <c>-v</c>/<c>--verbosity</c>.</summary>
    private static readonly HashSet<string> VerbosityValues =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "q",
            "quiet",
            "m",
            "minimal",
            "n",
            "normal",
            "d",
            "detailed",
            "diag",
            "diagnostic"
        };

    /// <summary>
    /// Canonicalizes the casing of a known <c>dotnet &lt;subcommand&gt;</c> invocation so
    /// Spectre.Console's case-sensitive routing matches (e.g. <c>dtk DOTNET BUILD</c> →
    /// <c>dotnet build</c>). Unknown/passthrough invocations are returned untouched, since
    /// their subcommand is forwarded verbatim to <c>dotnet</c>. A filtered subcommand typed without the
    /// <c>dotnet</c> word (<c>dtk test --filter X</c>, <c>dtk list package</c>) gets it, so every later step sees an
    /// ordinary <c>dotnet …</c> invocation; Spectre would otherwise answer "Unknown command 'test'. Did you mean
    /// 'reset'?". No dtk verb shares a name with a filtered subcommand's first token (pinned by
    /// <c>SubcommandRegistrationTests</c>), so none is ever prefixed.
    /// </summary>
    /// <param name="args">The raw CLI arguments.</param>
    internal static string[] Normalize(string[] args)
    {
        if (args.Length >= 1 &&
            !string.Equals(args[0], DotnetCommand, StringComparison.OrdinalIgnoreCase) &&
            DotnetSubcommands.TryMatch(args, out _))
        {
            args = [DotnetCommand, .. args];
        }

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
    /// and forward directly to <c>dotnet</c>: a subcommand dtk does not filter, or a filtered one whose run must
    /// stay unfiltered with the terminal attached, because it prompts
    /// (<see cref="PassthroughSubcommands.IsInteractiveFilteredRun"/>), prints output for a program to parse
    /// (<see cref="PassthroughSubcommands.IsMachineReadableRun"/>) or asks a tool for its own help
    /// (<see cref="PassthroughSubcommands.IsToolHelpRun"/>).
    /// </summary>
    /// <param name="args">The raw CLI arguments.</param>
    internal static bool IsPassthrough(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[0], DotnetCommand, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var dotnetArgs = args[1..];
        return !DotnetSubcommands.TryMatch(dotnetArgs, out _) ||
               PassthroughSubcommands.IsInteractiveFilteredRun(dotnetArgs) ||
               PassthroughSubcommands.IsMachineReadableRun(dotnetArgs) ||
               PassthroughSubcommands.IsToolHelpRun(dotnetArgs);
    }

    /// <summary>
    /// Returns the arguments a passthrough run hands to <c>dotnet</c>. A subcommand dtk does not filter is
    /// forwarded verbatim; a filtered one passed through anyway loses dtk's own options as under
    /// <see cref="RawOutputSwitch.EnvironmentVariable"/> (see <see cref="StripDtkOptions"/>), which dotnet would reject.
    /// </summary>
    /// <param name="args">The raw CLI arguments, for which <see cref="IsPassthrough"/> is <see langword="true"/>.</param>
    internal static string[] PassthroughArguments(string[] args)
    {
        var dotnetArgs = args[1..];
        return DotnetSubcommands.TryMatch(dotnetArgs, out _) ? StripDtkOptions(dotnetArgs) : dotnetArgs;
    }

    /// <summary>
    /// Returns <see langword="true"/> when a <c>dotnet</c> invocation asks for raw output through
    /// <see cref="RawOutputSwitch.EnvironmentVariable"/>, so that even a filtered subcommand is passed through.
    /// </summary>
    /// <param name="args">The raw CLI arguments.</param>
    /// <param name="dtkRawValue">The variable's value, or <see langword="null"/> when it is unset.</param>
    internal static bool IsRawRequested(string[] args, string? dtkRawValue)
    {
        return args.Length >= 1 &&
               string.Equals(args[0], DotnetCommand, StringComparison.OrdinalIgnoreCase) &&
               RawOutputSwitch.IsOnFor(dtkRawValue);
    }

    /// <summary>
    /// Removes dtk's options (<c>-v</c>, <c>--verbose</c>, <c>--vv</c>, <c>--show-log</c>, <c>-q</c>, <c>--quiet</c>,
    /// the test options, and <c>--tee</c> and <c>--max-lines</c> with their values) from arguments a raw run passes to
    /// dotnet, which would reject them. A <c>-v</c> followed by a dotnet verbosity level is dotnet's own and stays, as
    /// does help, which dotnet then prints; the user's own <c>--</c> and everything after it are forwarded verbatim. A
    /// tool-backed subcommand keeps its tool's short options and <c>--verbose</c>, <c>--quiet</c> and help.
    /// </summary>
    /// <param name="dotnetArgs">The arguments for dotnet, starting at the subcommand.</param>
    internal static string[] StripDtkOptions(string[] dotnetArgs)
    {
        var toolBacked = DotnetSubcommands.TryMatch(dotnetArgs, out var match) && IsToolBacked(match);
        var isStripped = new bool[dotnetArgs.Length];
        foreach (var option in FindDtkOptions(dotnetArgs, 0, toolBacked)
                     .Where(option => !HelpOptions.Contains(option.Name)))
        {
            Array.Fill(isStripped, true, option.Index, option.Length);
        }

        return [.. dotnetArgs.Where((_, i) => !isStripped[i])];
    }

    /// <summary>
    /// Returns the first value option of a filtered <c>dotnet</c> invocation that has no value: neither joined to it
    /// (<c>--tee=out.log</c>; an empty joined value, <c>--tee=</c> or <c>--max-lines:</c>, is none) nor in the next
    /// token (see <see cref="IsOptionValue"/>). For <c>--tee</c> that token is
    /// absent or an option other than a negative integer; for <c>--max-lines</c> it is anything but an integer
    /// (<c>--max-lines -1</c> has a value, which the settings then reject). Reporting it as dtk's usage error keeps
    /// <c>--tee --filter X</c> from reading <c>--filter</c> as the path, and <c>--max-lines MyApp.slnx</c> from
    /// dropping the project from a passed-through run, where nothing would validate the value.
    /// </summary>
    /// <param name="args">The raw CLI arguments, after <see cref="Normalize"/>.</param>
    /// <returns>The option's name, or <see langword="null"/> when every value option has its value.</returns>
    internal static string? MissingOptionValue(string[] args) =>
        TryMatchFilteredRun(args, out var match)
            ? FindDtkOptions(args, 1 + match.TokenCount, IsToolBacked(match))
                .Where(option => option.MissingValue)
                .Select(option => option.Name)
                .FirstOrDefault()
            : null;

    /// <summary>
    /// Returns the first test option (<c>--names</c>, <c>--show-output</c>, <c>--fail-on-empty</c>) on a filtered
    /// subcommand other than <c>dotnet test</c>, which is dtk's usage error rather than a token passed to dotnet or, on
    /// a tool-backed subcommand, to the tool.
    /// </summary>
    /// <param name="args">The raw CLI arguments, after <see cref="Normalize"/>.</param>
    /// <returns>The option's name, or <see langword="null"/> when there is none.</returns>
    internal static string? MisplacedTestOption(string[] args) =>
        TryMatchFilteredRun(args, out var match) &&
        !string.Equals(match.Name, DotnetSubcommands.Test, StringComparison.Ordinal)
            ? FindDtkOptions(args, 1 + match.TokenCount, IsToolBacked(match))
                .Select(option => option.Name)
                .FirstOrDefault(TestOnlyOptions.Contains)
            : null;

    /// <summary>
    /// Returns the first option a filtered subcommand passed through anyway (<c>DTK_RAW=1</c>, a prompt, a
    /// machine-readable run, a tool's help) cannot honour: <c>--tee</c>, in any form, which promises a file, or
    /// <c>--fail-on-empty</c>, which changes the verdict. Running without it would be a false green, so the run is
    /// refused.
    /// </summary>
    /// <param name="dotnetArgs">The arguments for dotnet, starting at the subcommand.</param>
    /// <returns>The option's name, or <see langword="null"/> when there is none or the subcommand is not filtered.</returns>
    internal static string? OptionRefusedOnPassthrough(string[] dotnetArgs) =>
        FilteredSubcommandOptionNames(dotnetArgs).FirstOrDefault(RefusedOnPassthrough.Contains);

    /// <summary>
    /// Returns the display options (<c>--max-lines</c>, <c>--names</c>, <c>--show-output</c>) a filtered subcommand
    /// passed through anyway drops, each once and in order, so each gets one warning.
    /// </summary>
    /// <param name="dotnetArgs">The arguments for dotnet, starting at the subcommand.</param>
    /// <returns>The options' names; empty when there are none or the subcommand is not filtered.</returns>
    internal static IReadOnlyList<string> OptionsDroppedOnPassthrough(string[] dotnetArgs) =>
    [
        .. FilteredSubcommandOptionNames(dotnetArgs)
            .Where(DroppedOnPassthrough.Contains)
            .Distinct(StringComparer.Ordinal)
    ];

    /// <summary>
    /// Checks dtk's options before anything runs. A value option without its value
    /// (<see cref="MissingOptionValue"/>) or a test option on another subcommand (<see cref="MisplacedTestOption"/>) is
    /// a usage error. A filtered subcommand passed through anyway — for a prompt, machine-readable output, a tool's
    /// help or <c>DTK_RAW=1</c>, in the order <c>Program</c> tests them — is refused for an option it cannot honour
    /// (<see cref="OptionRefusedOnPassthrough"/>), and warns once for each display option it drops.
    /// </summary>
    /// <param name="args">The raw CLI arguments, after <see cref="Normalize"/>.</param>
    /// <param name="dtkRawValue">The value of <see cref="RawOutputSwitch.EnvironmentVariable"/>, or <see langword="null"/>.</param>
    /// <returns>The lines for stderr, and whether the run stops with dtk's usage error without running the child.</returns>
    internal static OptionCheck CheckOptions(string[] args, string? dtkRawValue)
    {
        if (MissingOptionValue(args) is { } withoutValue)
        {
            var needed = string.Equals(withoutValue, MaxLinesOption, StringComparison.Ordinal) ? "a number" : "a value";
            return new OptionCheck([$"dtk: {withoutValue} needs {needed}"], IsUsageError: true);
        }

        if (MisplacedTestOption(args) is { } testOption)
        {
            return new OptionCheck([$"dtk: {testOption} applies only to dotnet test"], IsUsageError: true);
        }

        if (FilteredPassthroughReason(args, dtkRawValue) is not { } reason)
        {
            return new OptionCheck([], IsUsageError: false);
        }

        var dotnetArgs = args[1..];
        if (OptionRefusedOnPassthrough(dotnetArgs) is { } refused)
        {
            return new OptionCheck([$"dtk: {refused} needs a filtered run, but {reason}"], IsUsageError: true);
        }

        return new OptionCheck(
            [.. OptionsDroppedOnPassthrough(dotnetArgs).Select(dropped => $"dtk: {dropped} ignored: {reason}")],
            IsUsageError: false);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="token"/> is a verbosity level dotnet accepts
    /// (<c>q</c>, <c>m</c>, <c>n</c>, <c>d</c>, <c>diag</c> and their long names), ignoring case.
    /// </summary>
    /// <param name="token">A single raw CLI token.</param>
    internal static bool IsVerbosityValue(string token)
    {
        return VerbosityValues.Contains(token);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <c>args[index]</c> is <c>-v</c> and the token after it is a dotnet
    /// verbosity level. A bare <c>-v</c> (followed by anything else, or nothing) is dtk's verbose flag.
    /// </summary>
    /// <param name="args">The raw CLI arguments.</param>
    /// <param name="index">The index of the token to test.</param>
    private static bool IsDotnetVerbosityFlag(string[] args, int index)
    {
        return string.Equals(args[index], "-v", StringComparison.Ordinal) &&
               index + 1 < args.Length &&
               IsVerbosityValue(args[index + 1]);
    }

    /// <summary>
    /// Inserts <c>--</c> before dotnet-specific args so Spectre.Console forwards them
    /// as <c>Remaining.Raw</c> without treating them as its own options.
    /// DTK flags (<c>-v</c>, <c>--verbose</c>, <c>--vv</c>, <c>--show-log</c>, <c>-q</c>, <c>--quiet</c>, the test
    /// options and help) are partitioned ahead of the inserted <c>--</c>, and so are <c>--tee</c> and
    /// <c>--max-lines</c> together with their value, except a <c>-v</c> followed by a dotnet verbosity level
    /// (see <see cref="IsVerbosityValue"/>), which is dotnet's option and stays after it. A user's own <c>--</c>
    /// and everything after it is forwarded verbatim: Spectre consumes only the first (inserted) separator, so the
    /// user's separator survives into <c>Remaining.Raw</c> (e.g. <c>dotnet test -- RunConfiguration.X=1</c>).
    /// Returns the same array unchanged when no modification is needed.
    /// </summary>
    /// <param name="args">The raw CLI arguments.</param>
    internal static string[] InsertSeparator(string[] args)
    {
        if (!TryMatchFilteredRun(args, out var match))
        {
            return args;
        }

        var firstArgIndex = 1 + match.TokenCount;
        if (args.Length <= firstArgIndex)
        {
            return args;
        }

        var isDtkToken = new bool[args.Length];
        foreach (var option in FindDtkOptions(args, firstArgIndex, IsToolBacked(match)))
        {
            Array.Fill(isDtkToken, true, option.Index, option.Length);
        }

        var dtkFlags = new List<string>();
        var dotnetArgs = new List<string>();
        for (var i = firstArgIndex; i < args.Length; i++)
        {
            (isDtkToken[i] ? dtkFlags : dotnetArgs).Add(args[i]);
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

    /// <summary>
    /// Matches a filtered <c>dotnet</c> invocation: <c>dotnet</c> followed by a subcommand dtk filters.
    /// </summary>
    /// <param name="args">The raw CLI arguments.</param>
    /// <param name="match">The subcommand, or <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when the invocation is one.</returns>
    private static bool TryMatchFilteredRun(string[] args, out SubcommandMatch match)
    {
        if (args.Length >= 2 && string.Equals(args[0], DotnetCommand, StringComparison.OrdinalIgnoreCase))
        {
            return DotnetSubcommands.TryMatch(args[1..], out match);
        }

        match = default;
        return false;
    }

    /// <summary>
    /// Why a filtered subcommand is passed through although dtk filters it — the reason a refusal or warning gives —
    /// or <see langword="null"/> when it is filtered, or the invocation is not a filtered subcommand.
    /// </summary>
    /// <param name="args">The raw CLI arguments.</param>
    /// <param name="dtkRawValue">The value of <see cref="RawOutputSwitch.EnvironmentVariable"/>, or <see langword="null"/>.</param>
    private static string? FilteredPassthroughReason(string[] args, string? dtkRawValue)
    {
        if (!TryMatchFilteredRun(args, out _))
        {
            return null;
        }

        var dotnetArgs = args[1..];
        if (PassthroughSubcommands.PromptsForConfirmation(dotnetArgs))
        {
            return "the command asks for confirmation";
        }

        if (PassthroughSubcommands.IsInteractiveFilteredRun(dotnetArgs))
        {
            return "--interactive is set";
        }

        if (PassthroughSubcommands.IsMachineReadableRun(dotnetArgs))
        {
            return "the output is machine-readable";
        }

        if (PassthroughSubcommands.IsToolHelpRun(dotnetArgs))
        {
            return "the tool prints its own help";
        }

        return IsRawRequested(args, dtkRawValue) ? "DTK_RAW=1 is set" : null;
    }

    /// <summary>The names of dtk's options in a filtered subcommand's arguments; none for another subcommand.</summary>
    /// <param name="dotnetArgs">The arguments for dotnet, starting at the subcommand.</param>
    private static IEnumerable<string> FilteredSubcommandOptionNames(string[] dotnetArgs) =>
        DotnetSubcommands.TryMatch(dotnetArgs, out var match)
            ? FindDtkOptions(dotnetArgs, match.TokenCount, IsToolBacked(match)).Select(option => option.Name)
            : [];

    /// <summary>Whether <paramref name="subcommand"/> runs a tool that owns the options dtk does not need.</summary>
    /// <param name="subcommand">A matched filtered subcommand.</param>
    private static bool IsToolBacked(SubcommandMatch subcommand) =>
        subcommand.Name is { } name && DotnetSubcommands.ToolBacked.Contains(name);

    /// <summary>
    /// Finds dtk's options from <paramref name="start"/> up to the user's own <c>--</c>, after which nothing is dtk's.
    /// A value option spans its value too when the next token can be one (see <see cref="IsOptionValue"/>); a
    /// <c>-v</c> that names a dotnet verbosity level is dotnet's, and its level is never a dtk option either. On a
    /// tool-backed subcommand only <see cref="ToolBackedOptions"/>, the joined test options and the value options are
    /// dtk's.
    /// </summary>
    /// <param name="args">The arguments to scan.</param>
    /// <param name="start">The index of the first argument after the subcommand.</param>
    /// <param name="toolBacked">Whether the subcommand is in <see cref="DotnetSubcommands.ToolBacked"/>.</param>
    private static IEnumerable<DtkOption> FindDtkOptions(string[] args, int start, bool toolBacked)
    {
        var flags = toolBacked ? ToolBackedOptions : DtkOptions;
        var i = start;
        while (i < args.Length && !string.Equals(args[i], "--", StringComparison.Ordinal))
        {
            if (ValueOptionName(args[i]) is { } name)
            {
                // `--tee=` joins an empty value: none, and Spectre would not take the next token for one either.
                var isJoined = name.Length < args[i].Length;
                var hasJoinedValue = name.Length + 1 < args[i].Length;
                var hasNextValue = !isJoined && IsOptionValue(name, args, i + 1);
                var option = new DtkOption(name, i, hasNextValue ? 2 : 1, !hasJoinedValue && !hasNextValue);
                yield return option;
                i += option.Length;
                continue;
            }

            if (JoinedTestOptionName(args[i]) is { } joined)
            {
                yield return new DtkOption(joined, i, 1, MissingValue: false);
            }
            else if (flags.Contains(args[i]) && !IsDotnetVerbosityFlag(args, i))
            {
                yield return new DtkOption(args[i], i, 1, MissingValue: false);
            }

            i++;
        }
    }

    /// <summary>
    /// Returns the test option <paramref name="token"/> names with a value joined by <c>=</c> or <c>:</c>
    /// (<c>--names=true</c>), or <see langword="null"/> when it is not one. Spectre refuses a value on a flag, so the
    /// token is dtk's: it must never reach dotnet.
    /// </summary>
    /// <param name="token">A single raw CLI token.</param>
    private static string? JoinedTestOptionName(string token)
    {
        var separator = token.AsSpan().IndexOfAny('=', ':');
        if (separator < 0)
        {
            return null;
        }

        var name = token[..separator];
        return TestOnlyOptions.Contains(name) ? name : null;
    }

    /// <summary>
    /// Returns the value option <paramref name="token"/> names, alone or with its value joined by <c>=</c> or
    /// <c>:</c>, or <see langword="null"/> when it names none.
    /// </summary>
    /// <param name="token">A single raw CLI token.</param>
    private static string? ValueOptionName(string token)
    {
        var separator = token.AsSpan().IndexOfAny('=', ':');
        var name = separator < 0 ? token : token[..separator];
        return ValueOptions.Contains(name) ? name : null;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <c>args[index]</c> exists and can be <paramref name="option"/>'s value: an
    /// integer for <c>--max-lines</c>; for <c>--tee</c>, any token but an option, though a negative integer is a value.
    /// So <c>--tee --filter X</c> never reads <c>--filter</c> as the path, <c>--max-lines MyApp.slnx</c> never takes the
    /// project, and the user's own <c>--</c> is never taken for a value.
    /// </summary>
    /// <param name="option">The value option's name.</param>
    /// <param name="args">The raw CLI arguments.</param>
    /// <param name="index">The index of the token after the option.</param>
    private static bool IsOptionValue(string option, string[] args, int index)
    {
        if (index >= args.Length)
        {
            return false;
        }

        var isInteger = int.TryParse(args[index], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
        return isInteger ||
               (!string.Equals(option, MaxLinesOption, StringComparison.Ordinal) && !args[index].StartsWith('-'));
    }

    /// <summary>One of dtk's options found in an argument list.</summary>
    /// <param name="Name">The option's name, without a joined value.</param>
    /// <param name="Index">The index of its first token.</param>
    /// <param name="Length">How many tokens it spans: 2 for a value option followed by its value, otherwise 1.</param>
    /// <param name="MissingValue">Whether it is a value option given no value at all.</param>
    private readonly record struct DtkOption(string Name, int Index, int Length, bool MissingValue);

    /// <summary>What <see cref="CheckOptions"/> found.</summary>
    /// <param name="Messages">The lines for stderr: one usage error or refusal, or a warning per dropped option.</param>
    /// <param name="IsUsageError">Whether the run stops with dtk's usage error, without running the child.</param>
    internal readonly record struct OptionCheck(IReadOnlyList<string> Messages, bool IsUsageError);
}
