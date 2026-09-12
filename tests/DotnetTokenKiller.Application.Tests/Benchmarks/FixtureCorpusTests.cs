using DotnetTokenKiller.Benchmarks.Corpus;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Benchmarks;

public sealed class FixtureCorpusTests
{
    [Fact]
    public void Names_ExposesEveryEmbeddedFixture()
    {
        // Bound to a count, not a subset: a glob that silently stopped matching would otherwise
        // leave the corpus quietly smaller than the fixtures directory it mirrors.
        FixtureCorpus.Names.Should().HaveCount(16);
        FixtureCorpus.Names.Should().Contain("dotnet_build_errors.txt");
        FixtureCorpus.Names.Should().Contain("dotnet_list_package_raw.txt");
    }

    [Fact]
    public void EveryFixture_LoadsNonEmptyText()
    {
        foreach (var name in FixtureCorpus.Names)
        {
            FixtureCorpus.Load(name).Should().NotBeNullOrWhiteSpace("fixture {0} must load", name);
        }
    }

    [Fact]
    public void Load_UnknownFixture_ThrowsListingKnownNames()
    {
        var act = () => FixtureCorpus.Load("does_not_exist.txt");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does_not_exist.txt*dotnet_build_errors.txt*");
    }
}
