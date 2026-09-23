using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

/// <summary>
/// Every command family, as deterministic inputs: fixtures on stdin, a fake <c>dotnet</c>, or state the
/// case writes first. Real builds stay out, because their output depends on restore and build state.
/// </summary>
internal static class ParityCases
{
    private const string RtkHookSettings =
        """{"hooks":{"PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"rtk hook claude"}]}]}}""";

    /// <summary>Every provider <c>dtk init</c> accepts. Declared before the cases that use it, which read it at initialization.</summary>
    private static readonly string[] InitProviders =
        ["claude", "copilot", "copilot-cli", "gemini", "codex", "opencode", "antigravity", "cursor", "windsurf", "aider", "jetbrains"];

    private static readonly Dictionary<string, ParityCase> Portable = new(StringComparer.Ordinal)
    {
        ["version"] = Steps(["--version"]),
        ["help"] = Steps(["--help"]),
        ["pipe-build"] = new ParityCase(
        [
            new ParityStep(["pipe", "build", "--exit-code", "1"], "dotnet_build_errors.txt"),
            new ParityStep(["pipe", "build"], "dotnet_build_warnings.txt"),
            new ParityStep(["pipe", "build", "--vv", "--show-log"], "dotnet_build_success.txt"),
        ]),
        ["pipe-test"] = new ParityCase(
        [
            new ParityStep(["pipe", "test", "--exit-code", "1"], "dotnet_test_failures.txt"),
            new ParityStep(["pipe", "test"], "dotnet_test_all_pass.txt"),
        ]),
        ["pipe-restore"] = new ParityCase([new ParityStep(["pipe", "restore"], "dotnet_restore_raw.txt")]),
        ["pipe-clean"] = new ParityCase([new ParityStep(["pipe", "clean"], "dotnet_clean_raw.txt")]),
        ["pipe-format"] = new ParityCase(
            [new ParityStep(["pipe", "format", "--exit-code", "2"], "dotnet_format_violations_raw.txt")]),
        ["pipe-list-package"] = new ParityCase(
            [new ParityStep(["pipe", "list", "package"], "dotnet_list_package_outdated_raw.txt")]),
        ["gain"] = new ParityCase(
        [
            new ParityStep(["pipe", "build", "--exit-code", "1"], "dotnet_build_errors.txt"),
            new ParityStep(["pipe", "test", "--exit-code", "1"], "dotnet_test_failures.txt"),
            new ParityStep(["pipe", "restore"], "dotnet_restore_raw.txt"),
            new ParityStep(["gain"]),
            new ParityStep(["gain", "--json"]),
            new ParityStep(["gain", "--coverage"]),
            new ParityStep(["gain", "--export", "csv"]),
            new ParityStep(["gain", "--days", "7", "--command", "build"]),
        ]),
        // The second tokenizer loads a different vocabulary; its counts differ from cl100k_base's on this
        // fixture (707 input tokens against 710), so the tracking rows prove which one ran.
        ["tokenizer-o200k"] = new ParityCase(
        [
            new ParityStep(["config", "set", "tracking.tokenizer", "O200kBase"]),
            new ParityStep(["pipe", "build", "--exit-code", "1"], "dotnet_build_errors.txt"),
            new ParityStep(["gain", "--json"]),
        ]),
        ["log"] = new ParityCase(
        [
            new ParityStep(["pipe", "test", "--exit-code", "1"], "dotnet_test_failures.txt"),
            new ParityStep(["log", "--list", "--all"]),
            new ParityStep(["log", "--all", "--lines", "5"]),
        ]),
        ["config"] = Steps(
            ["config", "show"],
            ["config", "set", "display.emoji", "false"],
            ["config", "set", "tracking.enabled", "maybe"],
            ["config", "show"]),
        ["doctor"] = Steps(["doctor"]),
        ["completion"] = Steps(
            ["completion", "bash"], ["completion", "zsh"], ["completion", "fish"], ["completion", "powershell"]),
        ["init-project"] = new ParityCase(
        [
            .. InitProviders.Select(provider => new ParityStep(["init", provider, "--dir", "{project}"])),
            new ParityStep(["integrate", "claude", "--dir", "{project}"]),
        ], ArrangeRtk),
        ["init-uninstall"] = new ParityCase(
        [
            .. InitProviders.Select(provider => new ParityStep(["init", provider, "--dir", "{project}"])),
            .. InitProviders.Select(provider => new ParityStep(["init", provider, "--dir", "{project}", "--uninstall"])),
            new ParityStep(["init", "claude", "--dir", "{project}", "--uninstall"]),
        ], ArrangeRtk),
        ["spectre-built-ins"] = Steps(["cli", "version"], ["cli", "explain"], ["cli", "opencli"], ["--help-dump-opencli"]),
        ["passthrough"] = Steps(["dotnet", "--version"]),
        ["unknown-command"] = Steps(["frobnicate"]),
        ["reset"] = new ParityCase(
        [
            new ParityStep(["pipe", "build", "--exit-code", "1"], "dotnet_build_errors.txt"),
            new ParityStep(["reset", "--force", "--all"]),
            new ParityStep(["gain"]),
        ]),
    };

    private static readonly Dictionary<string, ParityCase> UnixOnly = new(StringComparer.Ordinal)
    {
        // On Windows, USERPROFILE does not move Environment.SpecialFolder.UserProfile, so a global
        // install would write into the runner's real profile.
        ["init-global"] = new ParityCase(
        [
            new ParityStep(["init", "claude", "--global"]),
            new ParityStep(["init", "copilot-cli", "--global"]),
        ], ArrangeRtk),
        ["wrapped"] = new ParityCase(
        [
            new ParityStep(["dotnet", "build"]),
            new ParityStep(["dotnet", "test"]),
            new ParityStep(["dotnet", "restore"]),
            new ParityStep(["dotnet", "clean"]),
            new ParityStep(["dotnet", "format"]),
            new ParityStep(["dotnet", "list", "package"]),
        ], ArrangeFakeDotnet),
    };

    public static TheoryData<string> PortableNames => [.. Portable.Keys];

    public static TheoryData<string> UnixOnlyNames => [.. UnixOnly.Keys];

    internal static ParityCase Get(string name) => Portable.TryGetValue(name, out var parityCase) ? parityCase : UnixOnly[name];

    private static ParityCase Steps(params string[][] invocations) =>
        new([.. invocations.Select(arguments => new ParityStep(arguments))]);

    private static void ArrangeRtk(ParitySandbox sandbox)
    {
        sandbox.WriteFile("home/.config/rtk/config.toml", "[hooks]\nexclude_commands = [\"git\"]\n");
        sandbox.WriteFile("proj/.claude/settings.json", RtkHookSettings);
        sandbox.WriteFile("home/.claude/settings.json", RtkHookSettings);
    }

    private static void ArrangeFakeDotnet(ParitySandbox sandbox)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The fake dotnet is a POSIX shell script.");
        }

        sandbox.CreateFakeDotnet(new Dictionary<string, (string Fixture, int ExitCode)>(StringComparer.Ordinal)
        {
            ["build"] = ("dotnet_build_errors.txt", 1),
            ["test"] = ("dotnet_test_failures.txt", 1),
            ["restore"] = ("dotnet_restore_raw.txt", 0),
            ["clean"] = ("dotnet_clean_raw.txt", 0),
            ["format"] = ("dotnet_format_violations_raw.txt", 2),
            ["list"] = ("dotnet_list_package_outdated_raw.txt", 0),
        });
    }
}
