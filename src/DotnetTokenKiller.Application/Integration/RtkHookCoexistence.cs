using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Detects an rtk PreToolUse hook and reconciles rtk's config so that dtk — not rtk — owns
/// <c>dotnet build/test/restore/clean/format</c> once dtk is integrated with Claude Code.
/// </summary>
internal sealed partial class RtkHookCoexistence
{
    private readonly string _userClaudeDir;

    // Read by the config-reconcile logic added in a later task; unused during detection-only.
#pragma warning disable S4487 // rtk config path is wired for the reconcile step landing in a later task
    private readonly string _rtkConfigPath;
#pragma warning restore S4487

    /// <summary>Creates an instance rooted at the real user Claude dir and rtk config path.</summary>
    public RtkHookCoexistence()
        : this(DefaultUserClaudeDir(), DefaultRtkConfigPath())
    {
    }

    /// <summary>Test seam: inject isolated paths so tests never touch the real machine.</summary>
    /// <param name="userClaudeDir">The user-level <c>~/.claude</c> directory to scan.</param>
    /// <param name="rtkConfigPath">The rtk <c>config.toml</c> path used by the reconcile step.</param>
    internal RtkHookCoexistence(string userClaudeDir, string rtkConfigPath)
    {
        _userClaudeDir = userClaudeDir;
        _rtkConfigPath = rtkConfigPath;
    }

    /// <summary>
    /// True when any of the user or project Claude settings files registers a PreToolUse hook whose
    /// command invokes <c>rtk … hook</c>.
    /// </summary>
    /// <param name="projectDirectory">The project root whose <c>.claude</c> settings are scanned.</param>
    internal bool IsRtkHookPresent(string projectDirectory)
    {
        string[] candidates =
        [
            Path.Combine(_userClaudeDir, "settings.json"),
            Path.Combine(_userClaudeDir, "settings.local.json"),
            Path.Combine(projectDirectory, ".claude", "settings.json"),
            Path.Combine(projectDirectory, ".claude", "settings.local.json"),
        ];

        return candidates.Any(FileRegistersRtkHook);
    }

    private static bool FileRegistersRtkHook(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path));
            if (root?["hooks"] is not JsonObject hooksObject || hooksObject["PreToolUse"] is not JsonArray preToolUse)
            {
                return false;
            }

            foreach (var entry in preToolUse)
            {
                if (entry?["hooks"] is not JsonArray hooks)
                {
                    continue;
                }

                foreach (var hook in hooks)
                {
                    if (hook?["command"] is JsonValue value
                        && value.TryGetValue<string>(out var command)
                        && RtkHookCommandRegex().IsMatch(command))
                    {
                        return true;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Tolerate malformed settings — never fail integration over another tool's file.
        }
        catch (IOException)
        {
            // Tolerate a settings file we cannot read — never fail integration over another tool's file.
        }

        return false;
    }

    private static string Home() =>
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string DefaultUserClaudeDir() =>
        Path.Combine(Home(), ".claude");

    private static string DefaultRtkConfigPath()
    {
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var configHome = string.IsNullOrEmpty(xdg) ? Path.Combine(Home(), ".config") : xdg;
        return Path.Combine(configHome, "rtk", "config.toml");
    }

    [GeneratedRegex(@"(?:^|[\s/\\""'])rtk(?:\.exe)?\s+hook\b")]
    private static partial Regex RtkHookCommandRegex();
}
