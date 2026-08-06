using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Application.Tests.UseCases;

public sealed class LogViewUseCaseTests
{
    private static DateTimeOffset At(int minute) => new(2026, 7, 28, 9, minute, 0, TimeSpan.Zero);

    private static TeeLogEntry Entry(int minute, string slug, string? cwd, string body = "")
    {
        var header = cwd is null
            ? null
            : new TeeLogHeader($"dotnet {slug}", cwd, 1, RunSource.Run, At(minute));
        return new TeeLogEntry($"/tee/{minute}_{slug}.log", header, body.Length, At(minute), slug);
    }

    private sealed class FakeStore(params TeeLogEntry[] entries) : ITeeLogStore
    {
        private readonly Dictionary<string, string> _bodies = [];

        public FakeStore WithBody(TeeLogEntry entry, string body)
        {
            _bodies[entry.FilePath] = body;
            return this;
        }

        public Task<IReadOnlyList<TeeLogEntry>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TeeLogEntry>>(
                [.. entries.OrderByDescending(e => e.TimestampUtc)]);

        public Task<string> ReadBodyAsync(TeeLogEntry entry, CancellationToken cancellationToken = default) =>
            Task.FromResult(_bodies.TryGetValue(entry.FilePath, out var body) ? body : string.Empty);
    }

    [Fact]
    public async Task SelectAsync_ScopesToTheGivenProject()
    {
        var mine = Entry(5, "build", "/proj/mine");
        var sut = new LogViewUseCase(new FakeStore(mine, Entry(6, "build", "/proj/other")));

        var selection = await sut.SelectAsync(new LogQuery(ProjectPath: "/proj/mine"));

        selection.Matches.Should().ContainSingle().Which.Should().Be(mine);
    }

    [Fact]
    public async Task SelectAsync_IgnoresATrailingSeparatorOnTheProjectPath()
    {
        var mine = Entry(5, "build", "/proj/mine");
        var sut = new LogViewUseCase(new FakeStore(mine));

        var selection = await sut.SelectAsync(
            new LogQuery(ProjectPath: "/proj/mine" + Path.DirectorySeparatorChar));

        selection.Matches.Should().ContainSingle();
    }

    [Fact]
    public async Task SelectAsync_DoesNotPrefixMatch()
    {
        // A log recorded at the repo root must not surface from a subdirectory: "the project" is
        // not something dtk can infer, so the rule stays exact and predictable.
        var sut = new LogViewUseCase(new FakeStore(Entry(5, "build", "/proj")));

        var selection = await sut.SelectAsync(new LogQuery(ProjectPath: "/proj/src/Sub"));

        selection.Matches.Should().BeEmpty();
    }

    [Fact]
    public async Task SelectAsync_ExcludesLegacyLogsFromAScopedQuery_AndCountsThem()
    {
        var sut = new LogViewUseCase(new FakeStore(
            Entry(5, "build", "/proj"),
            Entry(4, "build", null),
            Entry(3, "build", null)));

        var selection = await sut.SelectAsync(new LogQuery(ProjectPath: "/proj"));

        selection.Matches.Should().HaveCount(1);
        selection.LegacyExcluded.Should().Be(2);
    }

    [Fact]
    public async Task SelectAsync_IncludesLegacyLogs_WhenNotScoped()
    {
        var sut = new LogViewUseCase(new FakeStore(
            Entry(5, "build", "/proj"),
            Entry(4, "build", null)));

        var selection = await sut.SelectAsync(new LogQuery(ProjectPath: null));

        selection.Matches.Should().HaveCount(2);
        selection.LegacyExcluded.Should().Be(0);
    }

    [Fact]
    public async Task SelectAsync_FiltersBySubcommand_IncludingMultiTokenNames()
    {
        var listPackage = Entry(5, "list-package", "/proj");
        var sut = new LogViewUseCase(new FakeStore(listPackage, Entry(6, "build", "/proj")));

        var selection = await sut.SelectAsync(
            new LogQuery(Subcommand: "list package", ProjectPath: "/proj"));

        selection.Matches.Should().ContainSingle().Which.Should().Be(listPackage);
    }

    [Fact]
    public async Task SelectAsync_ReturnsNewestFirst()
    {
        var sut = new LogViewUseCase(new FakeStore(
            Entry(1, "build", "/proj"),
            Entry(9, "build", "/proj"),
            Entry(5, "build", "/proj")));

        var selection = await sut.SelectAsync(new LogQuery(ProjectPath: "/proj"));

        selection.Matches.Select(e => e.TimestampUtc)
            .Should().ContainInOrder(At(9), At(5), At(1));
    }

    [Fact]
    public async Task ViewAsync_ReturnsTheLastNLines_ByDefault()
    {
        var entry = Entry(5, "build", "/proj");
        var body = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"line {i}")) + "\n";
        var sut = new LogViewUseCase(new FakeStore(entry).WithBody(entry, body));

        var result = await sut.ViewAsync(new LogQuery(ProjectPath: "/proj", Lines: 3));

        result.View.Should().NotBeNull();
        result.View.TotalLines.Should().Be(10);
        result.View.ShownLines.Should().Be(3);
        result.View.Body.Should().Be("line 8\nline 9\nline 10");
    }

    [Fact]
    public async Task ViewAsync_ReturnsEverything_WhenFull()
    {
        var entry = Entry(5, "build", "/proj");
        var body = string.Join('\n', Enumerable.Range(1, 10).Select(i => $"line {i}")) + "\n";
        var sut = new LogViewUseCase(new FakeStore(entry).WithBody(entry, body));

        var result = await sut.ViewAsync(new LogQuery(ProjectPath: "/proj", Lines: 3, Full: true));

        result.View!.ShownLines.Should().Be(10);
        result.View.Body.Should().StartWith("line 1\n");
    }

    [Fact]
    public async Task ViewAsync_ShowsEverything_WhenTheWindowExceedsTheBody()
    {
        var entry = Entry(5, "build", "/proj");
        var sut = new LogViewUseCase(new FakeStore(entry).WithBody(entry, "only\ntwo\n"));

        var result = await sut.ViewAsync(new LogQuery(ProjectPath: "/proj", Lines: 100));

        result.View!.TotalLines.Should().Be(2);
        result.View.ShownLines.Should().Be(2);
    }

    [Fact]
    public async Task ViewAsync_HandlesAnEmptyBody()
    {
        var entry = Entry(5, "build", "/proj");
        var sut = new LogViewUseCase(new FakeStore(entry).WithBody(entry, string.Empty));

        var result = await sut.ViewAsync(new LogQuery(ProjectPath: "/proj"));

        result.View!.TotalLines.Should().Be(0);
        result.View.Body.Should().BeEmpty();
    }

    [Fact]
    public async Task ViewAsync_SelectsByIndex_OverTheFilteredSet()
    {
        var second = Entry(5, "build", "/proj");
        var sut = new LogViewUseCase(new FakeStore(
                Entry(9, "build", "/proj"), second, Entry(1, "build", "/proj"))
            .WithBody(second, "the second one\n"));

        var result = await sut.ViewAsync(new LogQuery(ProjectPath: "/proj", Index: 2));

        result.View!.Entry.Should().Be(second);
    }

    [Fact]
    public async Task ViewAsync_ReturnsNoView_WhenTheIndexIsOutOfRange()
    {
        var sut = new LogViewUseCase(new FakeStore(Entry(5, "build", "/proj")));

        var result = await sut.ViewAsync(new LogQuery(ProjectPath: "/proj", Index: 4));

        result.View.Should().BeNull();
        // The caller distinguishes "nothing matched" from "index too high" by this count.
        result.Selection.Matches.Should().HaveCount(1);
    }

    [Fact]
    public async Task ViewAsync_ReturnsNoView_WhenTheIndexIsBelowOne()
    {
        // The command validates this too, but the use case is also reachable directly (and from the
        // --list path), so a zero or negative index must not index into the match list.
        var sut = new LogViewUseCase(new FakeStore(Entry(5, "build", "/proj")));

        var result = await sut.ViewAsync(new LogQuery(ProjectPath: "/proj", Index: 0));

        result.View.Should().BeNull();
        result.Selection.Matches.Should().HaveCount(1);
    }

    [Fact]
    public async Task ViewAsync_DoesNotInventATrailingBlankLine_WhenTheBodyHasNoFinalNewline()
    {
        // A log from a run killed mid-line ends without a newline. Counting a phantom empty line
        // would make "showing last N of M" disagree with what the user can see.
        var entry = Entry(5, "build", "/proj");
        var sut = new LogViewUseCase(new FakeStore(entry).WithBody(entry, "line 1\nline 2"));

        var result = await sut.ViewAsync(new LogQuery(ProjectPath: "/proj", Full: true));

        result.View.Should().NotBeNull();
        result.View.TotalLines.Should().Be(2);
        result.View.Body.Should().Be("line 1\nline 2");
    }

    [Fact]
    public async Task SelectAsync_MatchesTheFilesystemRoot_WithoutReducingItToNothing()
    {
        // Trimming the trailing separator from "/" (or "C:\") would leave an empty string, which
        // matches nothing — so a run launched from the root would never find its own logs.
        var root = Path.GetPathRoot(Path.GetFullPath("/"))!;
        var entry = Entry(5, "build", root);
        var sut = new LogViewUseCase(new FakeStore(entry));

        var selection = await sut.SelectAsync(new LogQuery(ProjectPath: root));

        selection.Matches.Should().ContainSingle().Which.Should().Be(entry);
    }

    [Fact]
    public async Task ViewAsync_ReturnsNoView_WhenNothingMatched()
    {
        var sut = new LogViewUseCase(new FakeStore());

        var result = await sut.ViewAsync(new LogQuery(ProjectPath: "/proj"));

        result.View.Should().BeNull();
        result.Selection.Matches.Should().BeEmpty();
    }
}
