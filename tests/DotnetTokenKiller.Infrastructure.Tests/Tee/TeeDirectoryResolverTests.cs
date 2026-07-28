using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Infrastructure.Tee;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Tee;

/// <summary>Not parallelised: every case here manipulates the process-wide DTK_TEE_DIR variable.</summary>
[Collection("TeeDirectoryResolver")]
public sealed class TeeDirectoryResolverTests
{
    [Fact]
    public void Resolve_PrefersTheExplicitOverride_AboveEverythingElse()
    {
        WithEnvironment("/from/env", () =>
        {
            var config = new TeeConfig(TeeMode.Always, "/from/config");

            TeeDirectoryResolver.Resolve(config, "/from/override").Should().Be("/from/override");
        });
    }

    [Fact]
    public void Resolve_PrefersTheEnvironmentVariable_AboveConfig()
    {
        WithEnvironment("/from/env", () =>
        {
            var config = new TeeConfig(TeeMode.Always, "/from/config");

            TeeDirectoryResolver.Resolve(config, null).Should().Be("/from/env");
        });
    }

    [Fact]
    public void Resolve_UsesConfig_WhenNoOverrideOrEnvironmentVariable()
    {
        WithEnvironment(null, () =>
        {
            var config = new TeeConfig(TeeMode.Always, "/from/config");

            TeeDirectoryResolver.Resolve(config, null).Should().Be("/from/config");
        });
    }

    [Fact]
    public void Resolve_FallsBackToTheDefault_WhenConfigDirectoryIsEmpty()
    {
        WithEnvironment(null, () =>
        {
            var config = new TeeConfig(TeeMode.Always);

            TeeDirectoryResolver.Resolve(config, null).Should().Be(TeeDirectoryResolver.GetDefault());
        });
    }

    [Fact]
    public void GetDefault_IsRootedAndUnderADtkDirectory()
    {
        var path = TeeDirectoryResolver.GetDefault();

        Path.IsPathRooted(path).Should().BeTrue();
        path.Should().EndWith(Path.Combine("dtk", "tee"));
    }

    private static void WithEnvironment(string? value, Action body)
    {
        var original = Environment.GetEnvironmentVariable("DTK_TEE_DIR");
        try
        {
            Environment.SetEnvironmentVariable("DTK_TEE_DIR", value);
            body();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DTK_TEE_DIR", original);
        }
    }
}
