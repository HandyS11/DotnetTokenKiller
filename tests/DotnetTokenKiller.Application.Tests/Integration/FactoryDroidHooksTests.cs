using DotnetTokenKiller.Application.Integration;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class FactoryDroidHooksTests : IDisposable
{
    private const string LivePreToolUse =
        """{"PreToolUse":[{"matcher":"Execute","hooks":[{"type":"command","command":"lint"}]}]}""";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dtk-droid-hooks-{Guid.NewGuid()}", ".factory");

    private string HooksJson => Path.Combine(_dir, "hooks.json");
    private string LegacyHooksJson => Path.Combine(_dir, "hooks", "hooks.json");
    private string SettingsJson => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        var root = Path.GetDirectoryName(_dir)!;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, true);
        }
    }

    private static void Write(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    [Fact]
    public void Candidates_ListRootLegacyAndSettings_InDroidsOrder()
    {
        FactoryDroidHooks.Candidates(_dir).Should().Equal(
            (HooksJson, (string?)null), (LegacyHooksJson, (string?)null), (SettingsJson, "hooks"));
    }

    [Fact]
    public void ResolveTarget_NothingThere_CreatesRootHooksJson()
    {
        FactoryDroidHooks.ResolveTarget(_dir).Should().Be((HooksJson, (string?)null));
    }

    [Fact]
    public void ResolveTarget_SettingsRunsPreToolUseAndNoHooksJson_UsesSettings()
    {
        Write(SettingsJson, $$"""{"model":"x","hooks":{{LivePreToolUse}}}""");

        FactoryDroidHooks.ResolveTarget(_dir).Should().Be((SettingsJson, "hooks"));
    }

    [Fact]
    public void ResolveTarget_HooksJsonDefinesPreToolUse_WinsOverSettings()
    {
        Write(HooksJson, LivePreToolUse);
        Write(SettingsJson, $$"""{"hooks":{{LivePreToolUse}}}""");

        FactoryDroidHooks.ResolveTarget(_dir).Should().Be((HooksJson, (string?)null));
    }

    [Fact]
    public void ResolveTarget_HooksJsonWithoutPreToolUse_AndSettingsWithIt_UsesSettings()
    {
        Write(HooksJson, """{"PostToolUse":[]}""");
        Write(SettingsJson, $$"""{"hooks":{{LivePreToolUse}}}""");

        FactoryDroidHooks.ResolveTarget(_dir).Should().Be((SettingsJson, "hooks"));
    }

    [Fact]
    public void ResolveTarget_HooksJsonWithoutPreToolUse_AndNoSettingsHooks_UsesHooksJson()
    {
        Write(HooksJson, """{"PostToolUse":[]}""");
        Write(SettingsJson, """{"hooks":{"PreToolUse":[]}}""");

        FactoryDroidHooks.ResolveTarget(_dir).Should().Be((HooksJson, (string?)null));
    }

    [Fact]
    public void ResolveTarget_OnlyLegacyHooksJson_UsesLegacy()
    {
        Write(LegacyHooksJson, LivePreToolUse);

        FactoryDroidHooks.ResolveTarget(_dir).Should().Be((LegacyHooksJson, (string?)null));
    }

    [Fact]
    public void ResolveTarget_RootHooksJsonPresent_IgnoresLegacy()
    {
        Write(HooksJson, "{}");
        Write(LegacyHooksJson, LivePreToolUse);

        FactoryDroidHooks.ResolveTarget(_dir).Should().Be((HooksJson, (string?)null), "Droid reads the legacy file only when the root one is absent");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1]")]
    [InlineData("""{"hooks":[]}""")]
    public void ResolveTarget_UnreadableSettings_FallsBackToHooksJson(string settings)
    {
        Write(SettingsJson, settings);

        FactoryDroidHooks.ResolveTarget(_dir).Should().Be((HooksJson, (string?)null));
    }
}
