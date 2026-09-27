using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Application.Integration.Hooks;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration;

public sealed class ImportedClaudeHookTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dtk-imported-claude-hook-test-{Guid.NewGuid()}");

    public ImportedClaudeHookTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task IsRegisteredIn_FileRunningDtksClaudeHook_ReturnsTrue()
    {
        var other = Path.Combine(_tempDir, "other.json");
        var settings = Path.Combine(_tempDir, "settings.json");
        await File.WriteAllTextAsync(other, "{}");
        await File.WriteAllTextAsync(settings, $$"""{"command":"{{HookCommands.Invocation("claude")}}"}""");

        ImportedClaudeHook.IsRegisteredIn([Path.Combine(_tempDir, "missing.json"), other, settings]).Should().BeTrue();
        ImportedClaudeHook.IsRegisteredIn([other]).Should().BeFalse();
    }

    [Fact]
    public async Task IsRegisteredIn_UnreadableFile_CountsAsNotRegistered()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var settings = Path.Combine(_tempDir, "settings.json");
        await File.WriteAllTextAsync(settings, HookCommands.Invocation("claude"));
        File.SetUnixFileMode(settings, UnixFileMode.None);
        try
        {
            await File.ReadAllBytesAsync(settings);
            return; // Readable despite its mode (root): nothing to test.
        }
        catch (UnauthorizedAccessException)
        {
            // Expected: the branch under test.
        }

        ImportedClaudeHook.IsRegisteredIn([settings]).Should().BeFalse();
    }
}
