using System.Reflection;
using DotnetTokenKiller.Cli.Commands.Settings;
using FluentAssertions;
using Spectre.Console.Cli;
using Spectre.Console.Testing;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

/// <summary>
/// Binds the registered command surface to the documentation. Four commands shipped after v0.6.0
/// reached users undocumented because nothing failed when a command was added without docs; this is
/// the thing that fails.
/// </summary>
public sealed class DocsBindingTests
{
    /// <summary>
    /// Names deliberately absent from the docs, each with the reason. Keep this list short: it is
    /// the pressure valve that stops an inconvenient failure from getting the whole test disabled,
    /// not a place to park undocumented surface.
    /// </summary>
    private static readonly Dictionary<string, string> Allowed = new(StringComparer.Ordinal);

    [Fact]
    public void EveryRegisteredCommand_IsDocumented()
    {
        var readme = File.ReadAllText(Path.Combine(FindRepoRoot(), "README.md"));
        var articles = ReadArticles();

        foreach (var command in RegisteredCommandNames())
        {
            if (Allowed.ContainsKey(command))
            {
                continue;
            }

            readme.Should().Contain(command, "command '{0}' must appear in README.md", command);
            articles.Should().Contain(command, "command '{0}' must appear in a docfx article", command);
        }
    }

    [Fact]
    public void EveryLongFormOption_IsDocumented()
    {
        var articles = ReadArticles();

        foreach (var option in LongFormOptionNames())
        {
            if (Allowed.ContainsKey(option))
            {
                continue;
            }

            articles.Should().Contain(option, "option '{0}' must appear in a docfx article", option);
        }
    }

    /// <summary>All command names in the Spectre tree, walked depth-first (e.g. "log", "list package").</summary>
    private static List<string> RegisteredCommandNames()
    {
        var names = new List<string>();
        Walk([], names);
        return names;

        static void Walk(string[] path, List<string> into)
        {
            foreach (var name in ParseCommandsSection(RunHelp([.. path, "--help"])))
            {
                into.Add(name);
                Walk([.. path, name], into);
            }
        }
    }

    /// <summary>All long-form option names declared on the CLI's settings types.</summary>
    private static IEnumerable<string> LongFormOptionNames()
    {
        return typeof(GainCommandSettings).Assembly
            .GetTypes()
            .Where(t => t.Namespace == typeof(GainCommandSettings).Namespace)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .SelectMany(p => p.GetCustomAttributes<CommandOptionAttribute>())
            .SelectMany(a => a.LongNames)
            .Select(n => "--" + n)
            .Distinct(StringComparer.Ordinal);
    }

    private static string ReadArticles()
    {
        var articlesDir = Path.Combine(FindRepoRoot(), "docfx", "articles");
        return string.Join(
            "\n",
            Directory.EnumerateFiles(articlesDir, "*.md", SearchOption.AllDirectories).Select(File.ReadAllText));
    }

    private static List<string> ParseCommandsSection(string help)
    {
        var lines = help.Split('\n');
        var start = Array.FindIndex(lines, line => line.TrimEnd() == "COMMANDS:");

        if (start < 0)
        {
            return [];
        }

        return
        [
            .. lines
                .Skip(start + 1)
                .TakeWhile(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim().Split(' ')[0]),
        ];
    }

    private static string RunHelp(params string[] args)
    {
        var console = new TestConsole().Width(100);
        var app = new CommandApp();
        app.Configure(config =>
        {
            CliConfigurator.Configure(config, "1.2.3-test");
            config.Settings.Console = console;
        });

        app.Run(args);
        return console.Output;
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DotnetTokenKiller.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException(
            $"Could not locate DotnetTokenKiller.slnx above {AppContext.BaseDirectory}.");
    }
}
