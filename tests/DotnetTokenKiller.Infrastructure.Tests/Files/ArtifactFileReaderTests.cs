using DotnetTokenKiller.Infrastructure.Files;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Files;

public sealed class ArtifactFileReaderTests : IDisposable
{
    private const string ReportName = "mutation-report.json";

    private static readonly DateTimeOffset Start = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "dtk-artifact-" + Guid.NewGuid().ToString("N"));
    private readonly ArtifactFileReader _sut = new();

    public ArtifactFileReaderTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    private string Write(string relative, string content, DateTimeOffset lastWriteUtc)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, lastWriteUtc.UtcDateTime);

        // Native separators, as the reader returns them: on Windows the relative part's '/' becomes '\'.
        return Path.GetFullPath(path);
    }

    [Fact]
    public void FindNewest_IgnoresOlderThanStart()
    {
        Write("2026-11-04.11-00-00/reports/mutation-report.json", "old", Start.AddHours(-1));

        _sut.FindNewest(_root, ReportName, Start).Should().BeNull();

        var fresh = Write("2026-11-04.12-00-05/reports/mutation-report.json", "new", Start.AddSeconds(30));

        _sut.FindNewest(_root, ReportName, Start).Should().Be(fresh);
    }

    [Fact]
    public void FindNewest_PicksTheNewestOfSeveral_MatchingTheNameOnly()
    {
        Write("a/reports/mutation-report.json", "a", Start.AddSeconds(10));
        var newest = Write("b/reports/mutation-report.json", "b", Start.AddSeconds(20));
        Write("c/reports/mutation-report.html", "c", Start.AddSeconds(30));

        _sut.FindNewest(_root, ReportName, Start).Should().Be(newest);
    }

    [Fact]
    public void FindNewest_MissingDirectory_IsNull()
    {
        _sut.FindNewest(Path.Combine(_root, "absent"), ReportName, Start).Should().BeNull();
    }

    [Fact]
    public void TryReadText_ReadsTheFile_AndIsNullWhenMissing()
    {
        var path = Write("report.json", "{\"files\":{}}", Start);

        _sut.TryReadText(path).Should().Be("{\"files\":{}}");
        _sut.TryReadText(Path.Combine(_root, "absent.json")).Should().BeNull();
        _sut.TryReadText(_root).Should().BeNull();
        _sut.TryReadText(string.Empty).Should().BeNull();
    }
}
