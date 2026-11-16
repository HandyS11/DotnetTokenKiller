using DotnetTokenKiller.Domain.Files;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Domain.Tests.Files;

public sealed class WorkTreeChangesTests
{
    private static readonly string Root = Path.GetFullPath("/repo");

    [Fact]
    public void Contains_TheSameFileWrittenAnotherWay()
    {
        var changes = new WorkTreeChanges([Path.Combine(Root, "src", "A.cs")]);

        changes.Contains(Path.Combine(Root, "src", ".", "A.cs")).Should().BeTrue();
        changes.Contains(Path.Combine(Root, "tests", "..", "src", "A.cs")).Should().BeTrue();
        changes.Contains(Path.Combine(Root, "src", "B.cs")).Should().BeFalse();
    }

    [Fact]
    public void Contains_NoFile_IsFalse()
    {
        new WorkTreeChanges([Path.Combine(Root, "A.cs")]).Contains(string.Empty).Should().BeFalse();
    }

    [Fact]
    public void Contains_ComparesCaseOnlyOnLinux()
    {
        var changes = new WorkTreeChanges([Path.Combine(Root, "A.cs")]);

        changes.Contains(Path.Combine(Root, "a.cs")).Should().Be(!OperatingSystem.IsLinux());
    }

    [Fact]
    public void Count_CountsEachFileOnce()
    {
        new WorkTreeChanges([Path.Combine(Root, "A.cs"), Path.Combine(Root, ".", "A.cs"), Path.Combine(Root, "B.cs")])
            .Count.Should().Be(2);
    }
}
