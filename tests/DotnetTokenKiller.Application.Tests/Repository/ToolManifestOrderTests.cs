using System.Text.Json.Nodes;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Repository;

/// <summary>
/// Pins the order of this repository's own tool manifest. Since 0.8.0 the tool ships as a pointer
/// package that resolves a RID-specific package, and <c>dotnet tool restore</c> (SDK 10.0.401 and
/// every version before it) leaves the RID package's <c>project.assets.ridpackage.json</c> behind in
/// the asset directory it shares between the tools of one restore. Every tool listed after a
/// RID-specific one then reads that stale file and is reported as containing the wrong command:
/// <code>
/// The command "dotnet-stryker" specified in the tool manifest file is not contained in the package
/// with Package Id dotnet-stryker. The commands contained in the package are dtk.
/// </code>
/// The restore exits 1, which broke the Mutation Testing and Docs Publish workflows. It only bites a
/// cold tool resolver cache — a warm one skips the re-install that writes the stale file — so every
/// CI runner hits it and a developer's second run never does. Listing the tool last is the only
/// workaround that does not drop it from the manifest; nothing else in the file constrains the
/// order. See dotnet/sdk#53783 (open) and dotnet/sdk#53866 (the unmerged fix); once that ships in an
/// SDK this repository pins, the manifest can be reordered freely and this test deleted.
/// </summary>
public sealed class ToolManifestOrderTests
{
    /// <summary>The tool whose package resolves a RID-specific one, lower-cased as the manifest keys it.</summary>
    private const string RidSpecificTool = "dotnettokenkiller";

    [Fact]
    public void ToolManifest_ListsTheRidSpecificToolLast()
    {
        var manifest = JsonNode.Parse(File.ReadAllText(RepositoryPath(".config/dotnet-tools.json")));
        var tools = manifest?["tools"]?.AsObject();

        tools.Should().NotBeNull("the tool manifest must declare its tools");

        var names = tools.Select(tool => tool.Key).ToList();

        names.Should().Contain(
            RidSpecificTool,
            "this test guards where {0} sits in the manifest; drop the test with the tool",
            RidSpecificTool);

        names[^1].Should().Be(
            RidSpecificTool,
            "dotnet tool restore misattributes {0}'s command to every tool listed after it, so it "
            + "must stay last — `dotnet tool install` appends to the manifest, which puts the new "
            + "tool in the poisoned position",
            RidSpecificTool);
    }

    private static string RepositoryPath(string relativePath)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DotnetTokenKiller.slnx")))
            {
                return Path.Combine(dir.FullName, Path.Combine(relativePath.Split('/')));
            }
        }

        throw new InvalidOperationException(
            $"Could not locate DotnetTokenKiller.slnx above {AppContext.BaseDirectory}.");
    }
}
