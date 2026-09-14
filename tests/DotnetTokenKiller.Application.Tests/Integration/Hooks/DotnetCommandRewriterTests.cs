using DotnetTokenKiller.Application.Integration.Hooks;
using FluentAssertions;

namespace DotnetTokenKiller.Application.Tests.Integration.Hooks;

public sealed class DotnetCommandRewriterTests
{
    [Theory]
    [InlineData("dotnet build", "dtk dotnet build")]
    [InlineData("dotnet test", "dtk dotnet test")]
    [InlineData("dotnet restore", "dtk dotnet restore")]
    [InlineData("dotnet clean", "dtk dotnet clean")]
    [InlineData("dotnet format", "dtk dotnet format")]
    [InlineData("dotnet list package", "dtk dotnet list package")]
    [InlineData("dotnet list package --outdated", "dtk dotnet list package --outdated")]
    [InlineData("dotnet list   package", "dtk dotnet list   package")]
    [InlineData("dotnet   build", "dtk dotnet build")]
    [InlineData("dotnet\tformat", "dtk dotnet format")]
    [InlineData("dotnet\u00a0build", "dtk dotnet build")]
    [InlineData("cd src && dotnet test", "cd src && dtk dotnet test")]
    [InlineData("`dotnet build`", "`dtk dotnet build`")]
    [InlineData("echo dtk; dotnet build", "echo dtk; dtk dotnet build")]
    [InlineData("cd /tmp/dtk && dotnet test", "cd /tmp/dtk && dtk dotnet test")]
    [InlineData("(dotnet build)", "(dtk dotnet build)")]
    [InlineData("$(dotnet build)", "$(dtk dotnet build)")]
    [InlineData("{ dotnet build; }", "{ dtk dotnet build; }")]
    [InlineData("dotnet build\ndotnet test", "dtk dotnet build\ndtk dotnet test")]
    [InlineData("dotnet build && dotnet test", "dtk dotnet build && dtk dotnet test")]
    [InlineData("echo \u00e9 && dotnet build", "echo \u00e9 && dtk dotnet build")]
    [InlineData("echo \"a\\\" b\"; dotnet build", "echo \"a\\\" b\"; dtk dotnet build")]
    [InlineData("x=1 dotnet build", "x=1 dtk dotnet build")]
    [InlineData("sudo dotnet build", "sudo dtk dotnet build")]
    [InlineData("DTK dotnet build", "DTK dtk dotnet build")]
    [InlineData("dtk.EXE dotnet build", "dtk.EXE dtk dotnet build")]
    [InlineData("dotnet build-server shutdown", "dtk dotnet build-server shutdown")]
    public void Rewrite_QualifyingInvocation_IsPrefixedWithDtk(string command, string expected)
    {
        DotnetCommandRewriter.Rewrite(command).Should().Be(expected);
    }

    [Theory]
    [InlineData("/usr/lib64/dotnet/dotnet build")]
    [InlineData("./dotnet build")]
    [InlineData("mydotnet build")]
    [InlineData("DOTNET build")]
    [InlineData("dotnet tests")]
    [InlineData("dotnet build_x")]
    [InlineData("dotnet build\u00e9")]
    [InlineData("dotnet build\u00b2")]
    [InlineData("dotnet publish")]
    [InlineData("dotnet --info")]
    [InlineData("dotnet list  reference")]
    [InlineData("git commit -m \"fix dotnet build\"")]
    [InlineData("echo 'dotnet test'")]
    [InlineData("echo 'it\\'s'; dotnet build")]
    [InlineData("`dtk dotnet build`")]
    [InlineData("(dtk dotnet build)")]
    [InlineData("$(dtk dotnet build)")]
    [InlineData("echo hi;dtk dotnet build")]
    [InlineData("true&&dtk dotnet test")]
    [InlineData("ls|dtk dotnet format")]
    [InlineData("dtk  dotnet build")]
    [InlineData("~/.dotnet/tools/dtk dotnet build")]
    [InlineData(@"C:\tools\dtk.exe dotnet restore")]
    [InlineData("")]
    public void Rewrite_NonQualifyingInvocation_IsReturnedUnchanged(string command)
    {
        DotnetCommandRewriter.Rewrite(command).Should().BeSameAs(command);
    }

    [Theory]
    [InlineData("dotnet build", true)]
    [InlineData("dotnet test --filter \"A|B\"", true)]
    [InlineData("dotnet test --filter 'A&B'", true)]
    [InlineData("echo \\; dotnet build", true)]
    [InlineData("dotnet build && rm -rf x", false)]
    [InlineData("dotnet build; ls", false)]
    [InlineData("dotnet build | tee log", false)]
    [InlineData("$(dotnet build)", false)]
    [InlineData("dotnet build\nls", false)]
    [InlineData("dotnet build 2>&1", false)]
    public void IsSimpleCommand_MatchesThePythonHook(string command, bool expected)
    {
        DotnetCommandRewriter.IsSimpleCommand(command).Should().Be(expected);
    }
}
