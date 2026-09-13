using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

public sealed class AotWarningLogTests
{
    private const string SpectreTrim =
        "/root/.nuget/packages/spectre.console.cli/0.55.0/lib/net10.0/Spectre.Console.Cli.dll : warning IL2104: Assembly 'Spectre.Console.Cli' produced trim warnings. For more information see https://aka.ms/il2104 [/repo/src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj]";

    private const string SpectreAot =
        "/root/.nuget/packages/spectre.console.cli/0.55.0/lib/net10.0/Spectre.Console.Cli.dll : warning IL3053: Assembly 'Spectre.Console.Cli' produced AOT analysis warnings. [/repo/src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj]";

    private const string SpectreSingleFile =
        "/_/src/Spectre.Console.Cli/Internal/Modelling/CommandModel.cs(50): warning IL3000: Spectre.Console.Cli.CommandModel.GetApplicationFile(): 'System.Reflection.Assembly.Location.get' always returns an empty string for assemblies embedded in a single-file app. [/repo/src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj]";

    private const string Accepted = SpectreTrim + "\n" + SpectreAot + "\n" + SpectreSingleFile + "\n";

    [Fact]
    public void FindProblems_ExactlyTheAcceptedWarnings_ReportsNothing()
    {
        AotWarningLog.FindProblems("  Restored.\n" + Accepted + Accepted + "Build succeeded.\n").Should().BeEmpty(
            "MSBuild repeats warnings in its summary, and repeats are not new problems");
    }

    [Fact]
    public void FindProblems_WarningFromDtkCode_IsReported()
    {
        const string ours =
            "/repo/src/DotnetTokenKiller.Application/Integration/IntegratorHelpers.cs(386): Trim analysis warning IL2026: DotnetTokenKiller.Application.Integration.IntegratorHelpers.MergeJsonSettingsAsync(): Using member 'System.Text.Json.Nodes.JsonArray.Add<JsonObject>(JsonObject)' [/repo/src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj]";

        AotWarningLog.FindProblems(Accepted + ours).Should().ContainSingle().Which.Should().Contain("IL2026");
    }

    [Fact]
    public void FindProblems_AcceptedCodeFromAnotherAssembly_IsReported()
    {
        const string other =
            "/root/.nuget/packages/tomlyn/2.10.1/lib/net10.0/Tomlyn.dll : warning IL2104: Assembly 'Tomlyn' produced trim warnings. [/repo/src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj]";

        AotWarningLog.FindProblems(Accepted + other).Should().ContainSingle().Which.Should().Contain("Tomlyn");
    }

    [Fact]
    public void FindProblems_AcceptedCodeAsError_IsReported()
    {
        AotWarningLog.FindProblems(Accepted + SpectreTrim.Replace("warning IL2104", "error IL2104", StringComparison.Ordinal))
            .Should().ContainSingle().Which.Should().Contain("error IL2104");
    }

    [Fact]
    public void FindProblems_NoNativeCompile_ReportsEveryMissingWarning()
    {
        AotWarningLog.FindProblems("  dtk -> /repo/bin/dtk.dll\nBuild succeeded.\n").Should().HaveCount(3);
    }

    [AotPackLogFact]
    public async Task PackLog_HasOnlyTheAcceptedSpectreWarningsAsync()
    {
        var log = await File.ReadAllTextAsync(AotParitySkip.ReadRequired(AotPackLogFactAttribute.PackLogVariable));

        AotWarningLog.FindProblems(log).Should().BeEmpty(
            "Spectre.Console.Cli's IL2104, IL3053 and IL3000 are the only accepted trim or AOT warnings");
    }
}
