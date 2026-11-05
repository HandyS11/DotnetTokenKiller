using DotnetTokenKiller.Domain.Execution;

namespace DotnetTokenKiller.Application.Helpers;

/// <summary>
/// Adds to a <c>dotnet test</c> run the argument that makes its runner print each passed test's name, which
/// <c>--names</c> lists: VSTest's console logger at normal verbosity, or Microsoft.Testing.Platform's detailed
/// output. Only for a known runner, since each argument is an error or means something else under the other
/// (VSTest's <c>--logger</c> makes MTP exit 5; MTP's <c>--output Detailed</c> is VSTest's output directory).
/// </summary>
public static class TestArgumentInjector
{
    private const string Separator = "--";
    private const string LoggerOption = "--logger";
    private const string LoggerShortOption = "-l";
    private const string OutputOption = "--output";

    /// <summary>The arguments of <paramref name="testArgs"/> with the one that prints passed test names added.</summary>
    /// <param name="testArgs">The run's arguments, the subcommand included or not.</param>
    /// <param name="runner">The runner the run uses.</param>
    /// <returns>
    /// <paramref name="testArgs"/> unchanged when the runner is <see cref="TestRunner.Unknown"/> or the user already
    /// chose the setting (a <c>console</c> logger, an <c>--output</c>); otherwise the arguments with
    /// <c>--logger console;verbosity=normal</c> (VSTest) or <c>--output Detailed</c> (MTP) inserted before the user's
    /// own <c>--</c>, or at the end. Nothing after that <c>--</c> is read or changed: it belongs to the test run.
    /// </returns>
    public static IReadOnlyList<string> ForNames(IReadOnlyList<string> testArgs, TestRunner runner)
    {
        ArgumentNullException.ThrowIfNull(testArgs);

        var separator = IndexOfSeparator(testArgs);
        var options = testArgs.Take(separator).ToList();
        string[]? added = runner switch
        {
            TestRunner.VsTest when !HasConsoleLogger(options) => [LoggerOption, "console;verbosity=normal"],
            TestRunner.MicrosoftTestingPlatform when !HasOutput(options) => [OutputOption, "Detailed"],
            _ => null
        };

        return added is null ? testArgs : [.. options, .. added, .. testArgs.Skip(separator)];
    }

    private static int IndexOfSeparator(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (string.Equals(args[i], Separator, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return args.Count;
    }

    /// <summary>
    /// Whether a <c>--logger</c>/<c>-l</c> among <paramref name="options"/> names the console logger, its value given
    /// as the next argument or attached with <c>=</c> or <c>:</c>.
    /// </summary>
    /// <param name="options">The run's arguments before the user's <c>--</c>.</param>
    private static bool HasConsoleLogger(List<string> options)
    {
        for (var i = 0; i < options.Count; i++)
        {
            var value = OptionValue(options, i, LoggerOption) ?? OptionValue(options, i, LoggerShortOption);
            if (value?.StartsWith("console", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasOutput(List<string> options) =>
        options.Exists(o => string.Equals(o, OutputOption, StringComparison.Ordinal)
                            || o.StartsWith(OutputOption + "=", StringComparison.Ordinal)
                            || o.StartsWith(OutputOption + ":", StringComparison.Ordinal));

    /// <summary>
    /// The value of option <paramref name="name"/> when the argument at <paramref name="i"/> is that option: the
    /// next argument, or what follows an attached <c>=</c> or <c>:</c>; <see langword="null"/> otherwise.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <param name="i">The index of the argument to read.</param>
    /// <param name="name">The option's name.</param>
    private static string? OptionValue(List<string> args, int i, string name)
    {
        var arg = args[i];
        if (string.Equals(arg, name, StringComparison.Ordinal))
        {
            return i + 1 < args.Count ? args[i + 1] : null;
        }

        return arg.Length > name.Length && arg.StartsWith(name, StringComparison.Ordinal)
                                        && arg[name.Length] is '=' or ':'
            ? arg[(name.Length + 1)..]
            : null;
    }
}
