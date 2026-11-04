using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>Condenses <c>dotnet ef migrations</c> and <c>dotnet ef database</c> output.</summary>
/// <remarks>
/// Drops ef's build lines and the application's own <c>info</c>/<c>dbug</c>/<c>trce</c> log blocks (one count line
/// stands in for them), keeps every other line, and ends with one status line. A failed build never shows its
/// compile errors in ef's output, so the status line says how to get them. With <c>-v</c>/<c>--verbose</c> only the
/// build lines are dropped, since the user asked for the diagnostics.
/// </remarks>
/// <param name="rootPath">Optional root path used to shorten file paths in the output.</param>
public sealed partial class DotnetEfFilter(string? rootPath = null) : IContextualOutputFilter
{
    private const string BuildStarted = "Build started...";
    private const string BuildSucceeded = "Build succeeded.";
    private const string BuildFailedPrefix = "Build failed.";
    private const string LockNoise = "Acquiring an exclusive lock for migration application.";

    /// <summary>The root that paths are shown relative to: the given one, else the working directory.</summary>
    internal string RootPath => rootPath ?? Environment.CurrentDirectory;

    /// <summary>Applies the filter to the ef output, with nothing known about the run.</summary>
    /// <param name="strippedOutput">The ef output to filter, with ANSI escape sequences already stripped.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    public string Apply(string strippedOutput, int exitCode) =>
        Apply(strippedOutput, exitCode, FilterContext.Unknown).Output;

    /// <summary>Applies the filter to the ef output.</summary>
    /// <param name="strippedOutput">The ef output to filter, with ANSI escape sequences already stripped.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    /// <param name="context">The run's context: the verb, <c>-v</c>/<c>--verbose</c> and <c>-p</c>/<c>--project</c>.</param>
    public FilterResult Apply(string strippedOutput, int exitCode, FilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var command = EfCommand.From(context.Arguments);
        var scan = new Scan(command.Verbose, RootPath);
        foreach (var rawLine in (strippedOutput ?? string.Empty).Split('\n'))
        {
            scan.Add(rawLine.TrimEnd('\r'));
        }

        var sb = new StringBuilder();
        foreach (var line in scan.Lines())
        {
            sb.Append(line).Append('\n');
        }

        sb.Append(Status(command, exitCode, scan.BuildFailed, scan.Migrations)).Append('\n');
        return new FilterResult(sb.ToString());
    }

    private static string Status(EfCommand command, int exitCode, bool buildFailed, int migrations)
    {
        if (exitCode == 0)
        {
            return command.Verb == "list"
                ? string.Create(CultureInfo.InvariantCulture,
                    $"✓ {command.Name}: {Plural(migrations, "migration")}")
                : $"✓ {command.Name}: done";
        }

        var failed = string.Create(CultureInfo.InvariantCulture, $"✗ {command.Name} failed (exit {exitCode})");
        if (!buildFailed)
        {
            return failed;
        }

        var project = command.Project is null ? string.Empty : $" {command.Project}";
        return $"{failed}: build failed — run dtk dotnet build{project} to see the errors";
    }

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

    [GeneratedRegex(@"^(info|dbug|trce): .+\[\d+\]$")]
    private static partial Regex HiddenLogPattern();

    [GeneratedRegex(@"^\d{14}_\S+")]
    private static partial Regex MigrationPattern();

    /// <summary>Sorts the output lines into kept ones, hidden log blocks, build lines and migration lines.</summary>
    /// <param name="verbose">Whether log blocks are kept.</param>
    /// <param name="rootPath">The root that paths are shown relative to.</param>
    private sealed class Scan(bool verbose, string rootPath)
    {
        private readonly List<string> _kept = [];
        private int _hidden;
        private int _marker = -1;
        private bool _inHiddenBlock;

        public bool BuildFailed { get; private set; }

        public int Migrations { get; private set; }

        public List<string> Lines()
        {
            if (_marker >= 0)
            {
                _kept[_marker] = $"({Plural(_hidden, "log message")} hidden — dtk log)";
            }

            return _kept;
        }

        public void Add(string line)
        {
            if (_inHiddenBlock && line.Length > 0 && char.IsWhiteSpace(line[0]))
            {
                return;
            }

            _inHiddenBlock = false;
            if (line.Length == 0 || line is BuildStarted or BuildSucceeded || line.StartsWith(LockNoise, StringComparison.Ordinal))
            {
                return;
            }

            if (line.StartsWith(BuildFailedPrefix, StringComparison.Ordinal))
            {
                BuildFailed = true;
            }
            else if (!verbose && HiddenLogPattern().IsMatch(line))
            {
                Hide();
            }
            else
            {
                Keep(line);
            }
        }

        private void Hide()
        {
            _inHiddenBlock = true;
            _hidden++;
            if (_marker < 0)
            {
                _marker = _kept.Count;
                _kept.Add(string.Empty);
            }
        }

        private void Keep(string line)
        {
            if (MigrationPattern().IsMatch(line))
            {
                Migrations++;
            }

            _kept.Add(TextHelpers.ShortenPathsInLine(line, rootPath));
        }
    }

    /// <summary>What the run's arguments say about the ef command.</summary>
    /// <param name="Group">The command group, <c>migrations</c> or <c>database</c>, when known.</param>
    /// <param name="Verb">The verb after the group, when there is one.</param>
    /// <param name="Verbose">Whether ef was given <c>-v</c> or <c>--verbose</c>.</param>
    /// <param name="Project">The <c>-p</c>/<c>--project</c> value, when given.</param>
    private sealed record EfCommand(string? Group, string? Verb, bool Verbose, string? Project)
    {
        public string Name => string.Join(' ', new[] { "dotnet ef", Group, Verb }.Where(part => part is not null));

        public static EfCommand From(IReadOnlyList<string> arguments)
        {
            var hasGroup = arguments.Count > 0 && arguments[0] is "migrations" or "database";
            var index = hasGroup ? 1 : 0;
            var verb = index < arguments.Count && !arguments[index].StartsWith('-') ? arguments[index] : null;
            var group = hasGroup ? arguments[0] : GroupOf(verb);
            var verbose = false;
            string? project = null;
            for (var i = index; i < arguments.Count && arguments[i] != "--"; i++)
            {
                verbose |= arguments[i] is "-v" or "--verbose";
                project = ProjectAt(arguments, i) ?? project;
            }

            return new EfCommand(group, verb, verbose, project);
        }

        private static string? GroupOf(string? verb) => verb switch
        {
            "update" or "drop" => "database",
            "add" or "list" or "remove" or "script" or "bundle" => "migrations",
            _ => null,
        };

        private static string? ProjectAt(IReadOnlyList<string> arguments, int i)
        {
            if (arguments[i] is "-p" or "--project")
            {
                return i + 1 < arguments.Count ? arguments[i + 1] : null;
            }

            return arguments[i].StartsWith("--project=", StringComparison.Ordinal)
                ? arguments[i]["--project=".Length..]
                : null;
        }
    }
}
