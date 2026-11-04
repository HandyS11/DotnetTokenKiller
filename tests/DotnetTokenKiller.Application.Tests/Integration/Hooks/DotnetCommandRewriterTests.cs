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
    [InlineData("dotnet publish", "dtk dotnet publish")]
    [InlineData("dotnet publish src/App.csproj -c Release", "dtk dotnet publish src/App.csproj -c Release")]
    [InlineData("dotnet publish --interactive", "dtk dotnet publish --interactive")]
    [InlineData("dotnet pack", "dtk dotnet pack")]
    [InlineData("dotnet pack -o artifacts", "dtk dotnet pack -o artifacts")]
    [InlineData("dotnet list   package", "dtk dotnet list   package")]
    [InlineData("dotnet   build", "dtk dotnet build")]
    [InlineData("dotnet\tformat", "dtk dotnet format")]
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
    [InlineData("out=\"$(dotnet build 2>&1)\"", "out=\"$(dtk dotnet build 2>&1)\"")]
    [InlineData("echo \"$(echo \"$(dotnet test)\")\"", "echo \"$(echo \"$(dtk dotnet test)\")\"")]
    [InlineData("echo \"`dotnet build`\"", "echo \"`dtk dotnet build`\"")]
    [InlineData("dotnet build '$(x)'", "dtk dotnet build '$(x)'")]
    [InlineData("echo 1 # it's fine\ndotnet build", "echo 1 # it's fine\ndtk dotnet build")]
    [InlineData("cat <<EOF\ndotnet build\nEOF\ndotnet build", "cat <<EOF\ndotnet build\nEOF\ndtk dotnet build")]
    [InlineData("cat <<-EOF\n\tdotnet build\n\tEOF\ndotnet test", "cat <<-EOF\n\tdotnet build\n\tEOF\ndtk dotnet test")]
    [InlineData("cat <<'A' <<B\ndotnet build\nA\ndotnet test\nB\ndotnet clean", "cat <<'A' <<B\ndotnet build\nA\ndotnet test\nB\ndtk dotnet clean")]
    [InlineData("dotnet build <<EOF\nx\nEOF", "dtk dotnet build <<EOF\nx\nEOF")]
    [InlineData("cat <<< x; dotnet build", "cat <<< x; dtk dotnet build")]
    [InlineData("echo a#b; dotnet build", "echo a#b; dtk dotnet build")]
    [InlineData("echo $'it\\'s'; dotnet build", "echo $'it\\'s'; dtk dotnet build")]
    [InlineData("echo $$'\\'; dotnet build", "echo $$'\\'; dtk dotnet build")]
    [InlineData("x=$$'\\'; dotnet build #'", "x=$$'\\'; dtk dotnet build #'")]
    [InlineData("echo \"$$\"; dotnet build", "echo \"$$\"; dtk dotnet build")]
    [InlineData("echo $\"it's\"; dotnet build", "echo $\"it's\"; dtk dotnet build")]
    [InlineData("dtk\ndotnet build", "dtk\ndtk dotnet build")]
    [InlineData("dotnet list\tpackage", "dtk dotnet list\tpackage")]
    [InlineData("! dotnet build", "! dtk dotnet build")]
    [InlineData("echo \"hi!\"; dotnet build $[1]", "echo \"hi!\"; dtk dotnet build $[1]")]
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
    [InlineData("dotnet run")]
    [InlineData("dotnet package add Foo")]
    [InlineData("dotnet packages")]
    [InlineData("dotnet publisher")]
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
    [InlineData("cat > s.sh <<'EOF'\ndotnet build\nEOF")]
    [InlineData("cat > s.sh <<\"EOF\"\ndotnet build\nEOF")]
    [InlineData("cat > s.sh <<EOF\ndotnet build\nEOF")]
    [InlineData("cat > s.sh <<-EOF\n\tdotnet build\n\tEOF")]
    [InlineData("cat <<EOF\ndotnet build")]
    [InlineData("echo 1 # dotnet build")]
    [InlineData("echo \"\\$(dotnet build)\"")]
    [InlineData("echo '$(dotnet build)'")]
    [InlineData("\"$(dtk dotnet build)\"")]
    [InlineData("dotnet\nbuild")]
    [InlineData("dotnet\r\nbuild")]
    [InlineData("dotnet\u00a0build")]
    [InlineData("dotnet\vbuild")]
    [InlineData("dotnet\fbuild")]
    [InlineData("dotnet\u2028build")]
    [InlineData("dotnet list\u00a0package")]
    [InlineData("dotnet list\npackage")]
    [InlineData("cat <<EOF; dotnet\nbuild\nEOF")]
    public void Rewrite_NonQualifyingInvocation_IsReturnedUnchanged(string command)
    {
        DotnetCommandRewriter.Rewrite(command).Should().BeSameAs(command);
    }

    [Theory]
    [InlineData("rtk proxy dotnet test X")]
    [InlineData("rtk.exe proxy dotnet build")]
    [InlineData("rtk  proxy\tdotnet build")]
    [InlineData("~/.cargo/bin/rtk proxy dotnet build")]
    [InlineData("cd src && rtk proxy dotnet test")]
    [InlineData("command dotnet test X")]
    [InlineData("x=1 command dotnet build")]
    [InlineData("DTK_RAW=1 dotnet test X")]
    [InlineData("DTK_RAW=true dotnet build")]
    [InlineData("\\dotnet test X")]
    [InlineData("/usr/local/share/dotnet/dotnet test X")]
    public void Rewrite_RawOutputRequest_IsReturnedUnchanged(string command)
    {
        // An agent asking for dotnet's own output: run through rtk proxy or the command builtin, behind a
        // raw-enabling DTK_RAW assignment, or (as before) escaped or by path.
        DotnetCommandRewriter.Rewrite(command).Should().BeSameAs(command);
    }

    [Theory]
    [InlineData("DTK_RAW=0 dotnet test X", "DTK_RAW=0 dtk dotnet test X")]
    [InlineData("DTK_RAW=false dotnet build", "DTK_RAW=false dtk dotnet build")]
    [InlineData("DTK_RAW=FALSE dotnet build", "DTK_RAW=FALSE dtk dotnet build")]
    [InlineData("DTK_RAW= dotnet build", "DTK_RAW= dtk dotnet build")]
    [InlineData("DTK_RAW=\"0\" dotnet build", "DTK_RAW=\"0\" dtk dotnet build")]
    [InlineData("DTK_RAW=$RAW dotnet build", "DTK_RAW=$RAW dtk dotnet build")]
    [InlineData("DTK_RAW=1 x=1 dotnet build", "DTK_RAW=1 x=1 dtk dotnet build")]
    [InlineData("XDTK_RAW=1 dotnet build", "XDTK_RAW=1 dtk dotnet build")]
    [InlineData("rtk proxy ls && dotnet build", "rtk proxy ls && dtk dotnet build")]
    [InlineData("rtk proxy dotnet build && dotnet test", "rtk proxy dotnet build && dtk dotnet test")]
    [InlineData("command dotnet build; dotnet test", "command dotnet build; dtk dotnet test")]
    [InlineData("proxy dotnet build", "proxy dtk dotnet build")]
    [InlineData("rtk; proxy dotnet build", "rtk; proxy dtk dotnet build")]
    [InlineData("myrtk proxy dotnet build", "myrtk proxy dtk dotnet build")]
    [InlineData("env dotnet build", "env dtk dotnet build")]
    [InlineData("exec dotnet build", "exec dtk dotnet build")]
    [InlineData("sudo dotnet build", "sudo dtk dotnet build")]
    [InlineData("time dotnet build", "time dtk dotnet build")]
    public void Rewrite_NotARawOutputRequest_IsStillPrefixedWithDtk(string command, string expected)
    {
        // Only the dotnet that rtk proxy, command or a raw-enabling DTK_RAW runs directly is left alone; a
        // DTK_RAW the rewriter cannot read for certain is left for dtk to honour at run time.
        DotnetCommandRewriter.Rewrite(command).Should().Be(expected);
    }

    [Theory]
    [InlineData("rtk proxy dotnet test X")]
    [InlineData("rtk.exe proxy dotnet build")]
    [InlineData("command dotnet test X")]
    [InlineData("DTK_RAW=1 dotnet test X")]
    [InlineData("DTK_RAW=0 dotnet test X")]
    [InlineData("rtk proxy ls && dotnet build")]
    [InlineData("env dotnet build")]
    [InlineData("sudo dotnet build")]
    [InlineData("time dotnet build")]
    [InlineData("\\dotnet test X")]
    [InlineData("/usr/local/share/dotnet/dotnet test X")]
    public void RawOutputCases_AreNeitherSimpleNorAutoApprovable(string command)
    {
        // Pinned before the raw-output bypass existed: honouring it must not change what is auto-approved.
        DotnetCommandRewriter.IsSimpleCommand(command).Should().BeFalse();
        DotnetCommandRewriter.IsAutoApprovable(command).Should().BeFalse();
    }

    [Theory]
    [InlineData("dotnet build", true)]
    [InlineData("dotnet test --filter \"A|B\"", true)]
    [InlineData("dotnet test --filter 'A&B'", true)]
    [InlineData("echo \\; dotnet build", false)]
    [InlineData("dotnet build && rm -rf x", false)]
    [InlineData("dotnet build; ls", false)]
    [InlineData("dotnet build | tee log", false)]
    [InlineData("$(dotnet build)", false)]
    [InlineData("dotnet build\nls", false)]
    [InlineData("dotnet build 2>&1", false)]
    [InlineData("dotnet build \"$(rm -rf ~)\"", false)]
    [InlineData("dotnet build \"`id`\"", false)]
    [InlineData("dotnet build `id`", false)]
    [InlineData("dotnet build $(id)", false)]
    [InlineData("dotnet build \"$((1+2))\"", false)]
    [InlineData("echo \"$(echo \"$(dotnet test)\")\"", false)]
    [InlineData("dotnet build '$(x)'", true)]
    [InlineData("dotnet build '`id`'", true)]
    [InlineData("dotnet build \"\\$(x)\"", true)]
    [InlineData("dotnet build \\$\\(x\\)", true)]
    [InlineData("dotnet build \\; x", true)]
    [InlineData(" \tdotnet build $$ \"$HOME\" {a,b}", true)]
    [InlineData("dotnet build \"a;b\" # c; d", false)]
    [InlineData("dotnet build $$'\\'; rm -rf ~ #'", false)]
    [InlineData("dotnet build \"$$(id)\"", true)]
    [InlineData("rm -rf ~ dotnet build", false)]
    [InlineData("sudo dotnet build", false)]
    [InlineData("x=1 dotnet build", false)]
    [InlineData("dotnetx build", false)]
    [InlineData("dotnet", false)]
    [InlineData("dotnet build > ~/.bashrc", false)]
    [InlineData("dotnet build < input", false)]
    [InlineData("dotnet build $'a'", false)]
    [InlineData("dotnet build $\"a\"", false)]
    [InlineData("dotnet build ${x}", false)]
    [InlineData("dotnet build \"${x/'\"'/y}\"", false)]
    [InlineData("dotnet build # c", false)]
    [InlineData("dotnet build \"!#;echo PWNED;#\"", false)]
    [InlineData("dotnet build !!", false)]
    [InlineData("dotnet build \"hi!\"", false)]
    [InlineData("dotnet build '!x'", true)]
    [InlineData("dotnet build \\!x", true)]
    [InlineData("dotnet build $[HOME]", false)]
    [InlineData("dotnet build \"$[HOME]\"", false)]
    [InlineData("dotnet build '$[HOME]'", true)]
    [InlineData("dotnet build # c\nls", false)]
    [InlineData("dotnet build a\\ #; rm -rf ~", false)]
    [InlineData("dotnet build a#; rm -rf ~", false)]
    [InlineData("dotnet build $'\\''; rm -rf ~", false)]
    [InlineData("dotnet build <<EOF", false)]
    [InlineData("cat <<'EOF'\nx\nEOF", false)]
    [InlineData("dotnet build \"unterminated", false)]
    [InlineData("dotnet build 'unterminated", false)]
    public void IsSimpleCommand_OnlyAcceptsASingleInvocation(string command, bool expected)
    {
        DotnetCommandRewriter.IsSimpleCommand(command).Should().Be(expected);
    }

    [Fact]
    public void AutoApprovableSubcommands_AreExactlyTheOnesRewrittenBeforePublishAndPack()
    {
        // Pinned on purpose: a subcommand added to DotnetSubcommands gains a rewrite, never an auto-approval,
        // unless someone decides to widen this list and updates this test.
        DotnetCommandRewriter.AutoApprovableSubcommands.Should()
            .Equal("build", "test", "restore", "clean", "format", "list package");
    }

    [Theory]
    [InlineData("dotnet build", true)]
    [InlineData("dotnet test --no-build", true)]
    [InlineData("dotnet restore", true)]
    [InlineData("dotnet clean", true)]
    [InlineData("dotnet format --verify-no-changes", true)]
    [InlineData("dotnet list package --outdated", true)]
    [InlineData(" \tdotnet  list \t package", true)]
    [InlineData("dotnet publish -c Release -p:PublishProfile=Production", false)]
    [InlineData("dotnet publish", false)]
    [InlineData("dotnet pack -o artifacts", false)]
    [InlineData("dotnet run dotnet build", false)]
    [InlineData("dotnet list reference", false)]
    [InlineData("dotnet build && rm -rf x", false)]
    [InlineData("x=1 dotnet build", false)]
    public void IsAutoApprovable_RequiresASimpleCommandAndAnApprovableSubcommand(string command, bool expected)
    {
        DotnetCommandRewriter.IsAutoApprovable(command).Should().Be(expected);
    }

    [Theory]
    [InlineData("dotnet test --tee out.log")]
    [InlineData("dotnet test --tee=out.log")]
    [InlineData("dotnet build --tee x")]
    [InlineData("dotnet test --tee:out.log")]
    [InlineData("dotnet test --filter X --tee ~/.bashrc")]
    // The shell removes quotes and backslashes before dtk sees the word: each of these reaches dtk as `--tee`.
    [InlineData("dotnet test --\"tee\" ~/.bashrc")]
    [InlineData("dotnet test --t\\ee ~/.bashrc")]
    [InlineData("dotnet test --te''e ~/.bashrc")]
    [InlineData("dotnet build --t\"ee\"=/home/u/.bashrc")]
    public void IsAutoApprovable_IsFalse_WhenTheCommandWritesATeeFile(string command)
    {
        // `--tee` writes wherever its value points, so approving it would approve a file write that
        // IsSimpleCommand refuses as a redirection: `dotnet test --tee ~/.bashrc` is `dotnet test > ~/.bashrc`.
        DotnetCommandRewriter.IsSimpleCommand(command).Should().BeTrue("only the --tee rule refuses it");
        DotnetCommandRewriter.IsAutoApprovable(command).Should().BeFalse();
    }

    [Theory]
    // Brace expansion assembles it: `{--te,/home/u/.profil}e` becomes `--tee /home/u/.profile`.
    [InlineData("dotnet test {--te,/home/u/.profil}e")]
    [InlineData("dotnet test --filter \"A{B}\"")]
    [InlineData("dotnet build {a,b}")]
    // Globbing can match a file named `--tee` that the shell then hands over as the option.
    [InlineData("dotnet test --te[e] x")]
    [InlineData("dotnet test --filter Name~Foo*")]
    [InlineData("dotnet test x?")]
    // An unset parameter expands to nothing: `--te$9e` reaches dtk as `--tee`, with no variable set beforehand.
    [InlineData("dotnet test --te$9e ~/.bashrc")]
    [InlineData("dotnet test -$9-tee x")]
    [InlineData("dotnet test $X-tee x")]
    [InlineData("dotnet test \"$OPTS\"")]
    [InlineData("dotnet build $$")]
    // Fail-closed: an escaped `$` is refused too, rather than trusted to stay literal.
    [InlineData("dotnet test \\$X")]
    public void IsAutoApprovable_IsFalse_WhenTheShellExpandsTheCommand(string command)
    {
        // Expansion can assemble a `--tee` the text does not show, so no expanded command is auto-approved.
        DotnetCommandRewriter.IsSimpleCommand(command).Should().BeTrue("only the expansion rule refuses it");
        DotnetCommandRewriter.IsAutoApprovable(command).Should().BeFalse();
    }

    [Theory]
    [InlineData("dotnet test")]
    [InlineData("dotnet build --tl:off")]
    [InlineData("dotnet test --filter \"FullyQualifiedName~Foo\"")]
    [InlineData("dotnet test --filter 'Name=A|B' --no-build")]
    // Quoted or escaped, a glob character or a `$` is literal: the shell expands nothing.
    [InlineData("dotnet test --filter 'Name~Foo*'")]
    [InlineData("dotnet test --filter 'A$B'")]
    [InlineData("dotnet test --filter \"Name~Foo*\"")]
    [InlineData("dotnet test --filter Name~Foo\\*")]
    public void IsAutoApprovable_StaysTrue_WithoutATeeOption(string command)
    {
        DotnetCommandRewriter.IsAutoApprovable(command).Should().BeTrue();
    }

    [Fact]
    public void Rewrite_KeepsTheTeeOption()
    {
        DotnetCommandRewriter.Rewrite("dotnet test --tee x").Should().Be("dtk dotnet test --tee x");
    }
}
