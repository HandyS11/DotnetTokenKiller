using DotnetTokenKiller.Application.Helpers;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Helpers;

public sealed class ExecutableSearchTests
{
    private const string WindowsPathExt = ".COM;.EXE;.BAT;.CMD";

    /// <summary>A file-exists predicate that knows only <paramref name="files"/>.</summary>
    /// <param name="files">The paths that exist.</param>
    private static Func<string, bool> Existing(params string[] files)
    {
        var set = new HashSet<string>(files, StringComparer.Ordinal);
        return set.Contains;
    }

    [Fact]
    public void Find_Unix_ReturnsTheFirstMatchInPathOrder()
    {
        var found = ExecutableSearch.Find(
            "dtk", "/usr/bin:/home/me/.dotnet/tools:/opt/dtk", null, isWindows: false,
            Existing("/opt/dtk/dtk", "/home/me/.dotnet/tools/dtk"));

        found.Should().Be("/home/me/.dotnet/tools/dtk");
    }

    [Fact]
    public void Find_Unix_NotOnPath_ReturnsNull()
    {
        var found = ExecutableSearch.Find("dtk", "/usr/bin:/bin", null, isWindows: false, Existing("/opt/dtk/dtk"));

        found.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Find_NoPath_ReturnsNull(string? path)
    {
        var found = ExecutableSearch.Find("dtk", path, null, isWindows: false, _ => true);

        found.Should().BeNull("an unset PATH finds nothing, rather than falling back to some directory");
    }

    [Fact]
    public void Find_Unix_EmptyEntries_AreSkippedNotTreatedAsTheCurrentDirectory()
    {
        var probed = new List<string>();

        var found = ExecutableSearch.Find(
            "dtk", "::/usr/bin::", null, isWindows: false,
            candidate =>
            {
                probed.Add(candidate);
                return false;
            });

        found.Should().BeNull();
        probed.Should().Equal("/usr/bin/dtk");
    }

    [Fact]
    public void Find_Unix_EntryWithTrailingSlash_IsJoinedWithoutDoubling()
    {
        var found = ExecutableSearch.Find("dtk", "/opt/tools/", null, isWindows: false, Existing("/opt/tools/dtk"));

        found.Should().Be("/opt/tools/dtk");
    }

    [Fact]
    public void Find_Unix_DoesNotTryWindowsExtensions()
    {
        var found = ExecutableSearch.Find("dtk", "/opt/tools", WindowsPathExt, isWindows: false, Existing("/opt/tools/dtk.exe"));

        found.Should().BeNull();
    }

    [Fact]
    public void Find_Windows_PrefersDtkExeInEachDirectory()
    {
        var found = ExecutableSearch.Find(
            "dtk", @"C:\tools", WindowsPathExt, isWindows: true,
            Existing(@"C:\tools\dtk.CMD", @"C:\tools\dtk.exe"));

        found.Should().Be(@"C:\tools\dtk.exe");
    }

    [Fact]
    public void Find_Windows_FallsBackToPathExtExtensions()
    {
        var found = ExecutableSearch.Find(
            "dtk", @"C:\Windows;C:\tools", WindowsPathExt, isWindows: true, Existing(@"C:\tools\dtk.CMD"));

        found.Should().Be(@"C:\tools\dtk.CMD");
    }

    [Fact]
    public void Find_Windows_DirectoryOrderWinsOverExtensionOrder()
    {
        // cmd walks PATH directory by directory, trying every extension in each, so an earlier
        // directory's dtk.cmd shadows a later directory's dtk.exe.
        var found = ExecutableSearch.Find(
            "dtk", @"C:\first;C:\second", WindowsPathExt, isWindows: true,
            Existing(@"C:\first\dtk.BAT", @"C:\second\dtk.exe"));

        found.Should().Be(@"C:\first\dtk.BAT");
    }

    [Fact]
    public void Find_Windows_NoPathExt_UsesTheDefaultExtensions()
    {
        var found = ExecutableSearch.Find("dtk", @"C:\tools", null, isWindows: true, Existing(@"C:\tools\dtk.CMD"));

        found.Should().Be(@"C:\tools\dtk.CMD");
    }

    [Fact]
    public void Find_Windows_IgnoresAnExtensionlessFile()
    {
        var found = ExecutableSearch.Find("dtk", @"C:\tools", WindowsPathExt, isWindows: true, Existing(@"C:\tools\dtk"));

        found.Should().BeNull();
    }

    [Fact]
    public void Find_Windows_QuotedAndEmptyEntries_AreUnquotedAndSkipped()
    {
        var found = ExecutableSearch.Find(
            "dtk", @";""C:\Program Files\dtk"";;", WindowsPathExt, isWindows: true,
            Existing(@"C:\Program Files\dtk\dtk.exe"));

        found.Should().Be(@"C:\Program Files\dtk\dtk.exe");
    }

    [Fact]
    public void FindOnProcessPath_FindsAnExecutableFileOnThisProcessPath()
    {
        // The environment-reading wrapper, against the real file system: the test host itself is
        // not on PATH, but a shell every supported OS ships is.
        var shell = OperatingSystem.IsWindows() ? "cmd" : "sh";

        var found = ExecutableSearch.FindOnProcessPath(shell);

        found.Should().NotBeNull();
        Path.IsPathRooted(found).Should().BeTrue();
    }
}
