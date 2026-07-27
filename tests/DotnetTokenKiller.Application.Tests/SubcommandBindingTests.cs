using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Domain;
using DotnetTokenKiller.Domain.Filters;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotnetTokenKiller.Application.Tests;

/// <summary>
/// Binds the Application layer's restatements of the supported subcommands back to
/// <see cref="DotnetSubcommands"/>. These are the seams generation cannot close: a registration
/// that is simply absent produces no compile error and no runtime error until a user hits it.
/// </summary>
public sealed class SubcommandBindingTests
{
    [Fact]
    public void EverySubcommand_ResolvesAKeyedOutputFilter()
    {
        var provider = new ServiceCollection().AddApplication().BuildServiceProvider();

        foreach (var subcommand in DotnetSubcommands.Ordered)
        {
            var filter = provider.GetKeyedService<IOutputFilter>(subcommand);

            filter.Should().NotBeNull(
                "'{0}' is a canonical subcommand, so AddApplication must register a filter keyed to it",
                subcommand);
        }
    }

    [Fact]
    public void NoFilter_IsRegisteredUnderANonCanonicalKey()
    {
        var services = new ServiceCollection().AddApplication();

        var keys = services
            .Where(descriptor => descriptor.ServiceType == typeof(IOutputFilter))
            .Select(descriptor => descriptor.ServiceKey)
            .OfType<string>();

        keys.Should().BeEquivalentTo(DotnetSubcommands.Ordered);
    }

    [Fact]
    public void GeneratedHooks_DeclareExactlyTheCanonicalSubcommands()
    {
        // Pinned to the literal, known-good bytes rather than recomputed from
        // DotnetSubcommands.Sorted: if the source list shrinks or grows, the expectation must
        // not shrink or grow in lockstep with it, or this test could never fail. Adding a
        // subcommand means updating this literal by hand — that is the intended tripwire, since
        // it forces whoever adds one to also regenerate the committed hook.
        const string expectedTuple = "_DTK_SUBCOMMANDS = (\"build\", \"clean\", \"format\", \"restore\", \"test\")";

        foreach (var hook in new[]
                 {
                     HookScriptTemplates.ClaudeHook,
                     HookScriptTemplates.GeminiHook,
                     HookScriptTemplates.CopilotCliHook
                 })
        {
            hook.Should().Contain(expectedTuple);
        }
    }

    [Fact]
    public void GeneratedHooks_DocumentEveryCanonicalSubcommand()
    {
        // Pinned to the literal, known-good bytes for the same reason as the tuple assertion
        // above: deriving the expected alternation from DotnetSubcommands.Ordered would make the
        // test move in lockstep with the thing it is supposed to be pinning. Adding a
        // subcommand requires updating this literal, which forces regenerating the hooks too.
        const string expected = "rewrites `dotnet build|test|restore|clean|format`";

        HookScriptTemplates.ClaudeHook.Should().Contain(expected);
        HookScriptTemplates.GeminiHook.Should().Contain(expected);
        HookScriptTemplates.CopilotCliHook.Should().Contain(expected);
    }

    [Fact]
    public void RepoClaudeHook_IsByteIdenticalToTheGeneratedHook()
    {
        var repoRoot = FindRepoRoot();
        var hookPath = Path.Combine(repoRoot, ".claude", "hooks", "dotnet-to-dtk.py");

        File.Exists(hookPath).Should().BeTrue("this repo ships its own copy of the Claude hook at {0}", hookPath);

        var committed = File.ReadAllText(hookPath);

        committed.Should().Be(
            HookScriptTemplates.ClaudeHook,
            "the committed hook must be regenerated whenever the template changes, or this repo's own "
            + "agent sessions silently stop rewriting the newest subcommand");
    }

    /// <summary>
    /// Walks up from the test assembly to the directory holding <c>DotnetTokenKiller.slnx</c>.
    /// xunit 2.x has no runtime skip, and the test project only ever runs from inside the repo,
    /// so not finding it is a failure rather than a skip.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No ancestor of <see cref="AppContext.BaseDirectory"/> contains <c>DotnetTokenKiller.slnx</c>.
    /// </exception>
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
