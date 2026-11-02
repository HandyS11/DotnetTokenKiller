using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Repository;

/// <summary>
/// Pins the encoding of the committed Verify snapshots. The repository writes no byte order mark (see
/// <c>.editorconfig</c>), and Verify's default is to write one into any snapshot that holds a non-ASCII
/// character, which the filters' <c>✓</c> and <c>✗</c> status lines all do; a snapshot accepted by moving
/// a <c>.received</c> file into place then carried one, and it differed from every snapshot beside it.
/// <c>VerifyInit</c> turns that default off in each test project that uses Verify.
/// </summary>
public sealed class SnapshotEncodingTests
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    [Fact]
    public void CommittedSnapshots_StartWithoutAByteOrderMark()
    {
        var snapshots = Directory
            .EnumerateFiles(RepositoryPath("tests"), "*.verified.*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        snapshots.Should().NotBeEmpty("this test guards the snapshots; a path that matched none would guard nothing");

        var withBom = snapshots.Where(path => File.ReadAllBytes(path).AsSpan().StartsWith(Utf8Bom)).ToList();

        withBom.Should().BeEmpty("a snapshot is written without a byte order mark");
    }

    private static string RepositoryPath(string relativePath)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DotnetTokenKiller.slnx")))
            {
                return Path.Combine(dir.FullName, relativePath);
            }
        }

        throw new InvalidOperationException($"Could not locate DotnetTokenKiller.slnx above {AppContext.BaseDirectory}.");
    }
}
