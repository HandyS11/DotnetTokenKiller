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
    [InlineData("dotnet list ISDM.slnx package --vulnerable", "dtk dotnet list ISDM.slnx package --vulnerable")]
    [InlineData("dotnet list src/App/App.csproj  package", "dtk dotnet list src/App/App.csproj  package")]
    [InlineData("dotnet publish", "dtk dotnet publish")]
    [InlineData("dotnet publish src/App.csproj -c Release", "dtk dotnet publish src/App.csproj -c Release")]
    [InlineData("dotnet publish --interactive", "dtk dotnet publish --interactive")]
    [InlineData("dotnet pack", "dtk dotnet pack")]
    [InlineData("dotnet pack -o artifacts", "dtk dotnet pack -o artifacts")]
    [InlineData("dotnet tool restore", "dtk dotnet tool restore")]
    [InlineData("dotnet ef migrations add Initial -v", "dtk dotnet ef migrations add Initial -v")]
    [InlineData("dotnet ef database drop", "dtk dotnet ef database drop")]
    [InlineData("dotnet stryker --break-at 80", "dtk dotnet stryker --break-at 80")]
    [InlineData("dotnet jb cleanupcode App.slnx", "dtk dotnet jb cleanupcode App.slnx")]
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
    [InlineData("dotnet tool run x")]
    [InlineData("dotnet tool install -g dotnet-ef")]
    [InlineData("dotnet ef dbcontext list")]
    [InlineData("dotnet jb inspectcode App.slnx")]
    [InlineData("dotnet strykers")]
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
    [InlineData("dotnet list ISDM.slnx reference")]
    [InlineData("dotnet list ISDM.slnx\npackage")]
    [InlineData("dotnet list a.slnx b package")]
    [InlineData("dotnet list -v package")]
    [InlineData("dotnet list \"My App.slnx\" package")]
    [InlineData("dotnet list $SLN package")]
    [InlineData("dotnet list x;package")]
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
    [InlineData("rtk; dotnet build", "rtk; dtk dotnet build")]
    [InlineData("env dotnet build", "env dtk dotnet build")]
    [InlineData("exec dotnet build", "exec dtk dotnet build")]
    [InlineData("time dotnet build", "time dtk dotnet build")]
    public void Rewrite_NotARawOutputRequest_IsStillPrefixedWithDtk(string command, string expected)
    {
        // Only the dotnet that rtk proxy, command or a raw-enabling DTK_RAW runs directly is left alone; a
        // DTK_RAW the rewriter cannot read for certain is left for dtk to honour at run time.
        DotnetCommandRewriter.Rewrite(command).Should().Be(expected);
    }

    [Theory]
    [InlineData("A=1 B=2 dotnet test", "A=1 B=2 dtk dotnet test")]
    [InlineData("env A=1 dotnet build", "env A=1 dtk dotnet build")]
    [InlineData("env -u A B=1 dotnet build", "env -u A B=1 dtk dotnet build")]
    [InlineData("env --unset=A dotnet build", "env --unset=A dtk dotnet build")]
    [InlineData("time -p dotnet build", "time -p dtk dotnet build")]
    [InlineData("/usr/bin/time dotnet build", "/usr/bin/time dtk dotnet build")]
    [InlineData("timeout 600 dotnet test", "timeout 600 dtk dotnet test")]
    [InlineData("timeout 1.5m dotnet test", "timeout 1.5m dtk dotnet test")]
    [InlineData("timeout -k 5 1h dotnet test", "timeout -k 5 1h dtk dotnet test")]
    [InlineData("timeout --foreground -s KILL 30s dotnet test", "timeout --foreground -s KILL 30s dtk dotnet test")]
    [InlineData("timeout --kill-after=5s 10m dotnet test", "timeout --kill-after=5s 10m dtk dotnet test")]
    [InlineData("nice dotnet build", "nice dtk dotnet build")]
    [InlineData("nice -n 10 dotnet build", "nice -n 10 dtk dotnet build")]
    [InlineData("nice -5 dotnet build", "nice -5 dtk dotnet build")]
    [InlineData("nohup dotnet test", "nohup dtk dotnet test")]
    [InlineData("time timeout 60 dotnet test", "time timeout 60 dtk dotnet test")]
    [InlineData("x=1 timeout 60 dotnet test", "x=1 timeout 60 dtk dotnet test")]
    [InlineData("if dotnet build; then echo ok; fi", "if dtk dotnet build; then echo ok; fi")]
    [InlineData("if ! dotnet build; then exit 1; fi", "if ! dtk dotnet build; then exit 1; fi")]
    [InlineData("while ! dotnet test; do :; done", "while ! dtk dotnet test; do :; done")]
    [InlineData("until dotnet test; do sleep 1; done", "until dtk dotnet test; do sleep 1; done")]
    [InlineData("if x; then dotnet build; else dotnet test; fi", "if x; then dtk dotnet build; else dtk dotnet test; fi")]
    [InlineData("if a; then :; elif dotnet build; then :; fi", "if a; then :; elif dtk dotnet build; then :; fi")]
    [InlineData("for p in a b; do dotnet build $p; done", "for p in a b; do dtk dotnet build $p; done")]
    public void Rewrite_BehindWhatRunsItHereWithTheSamePath_IsPrefixedWithDtk(string command, string expected)
    {
        // Assignments, shell keywords, and the launchers that run their command in this shell's environment.
        DotnetCommandRewriter.Rewrite(command).Should().Be(expected);
    }

    [Theory]
    // A word that expands to a program: `$D dtk dotnet build` hands dtk the arguments `dtk dotnet build` when $D is a
    // dtk binary, which it rejects as an unknown command.
    [InlineData("$D dotnet build")]
    [InlineData("\"$D\" dotnet build")]
    [InlineData("${DTK} dotnet build")]
    [InlineData("$(which dtk) dotnet build")]
    // Text, not a command.
    [InlineData("echo dotnet build")]
    [InlineData("printf '%s' x; echo dotnet build")]
    // Another machine, container or user, which may have no dtk: sudo resets PATH to its secure_path.
    [InlineData("ssh host dotnet build")]
    [InlineData("docker run --rm sdk dotnet build")]
    [InlineData("sudo dotnet build")]
    [InlineData("sudo -E dotnet build")]
    // Programs that are not dtk under another spelling, or not rtk's raw-output request.
    [InlineData("DTK dotnet build")]
    [InlineData("dtk.EXE dotnet build")]
    [InlineData("proxy dotnet build")]
    [InlineData("myrtk proxy dotnet build")]
    [InlineData("rtk; proxy dotnet build")]
    [InlineData("echo x | xargs dotnet build")]
    [InlineData("rm -rf ~ dotnet build")]
    // A launcher whose arguments are not the plain ones it is known to take.
    [InlineData("env -i dotnet build")]
    [InlineData("env -C src dotnet build")]
    [InlineData("time -o out.txt dotnet build")]
    [InlineData("timeout dotnet build")]
    [InlineData("timeout -s dotnet build")]
    [InlineData("nice -n dotnet build")]
    [InlineData("exec -a name dotnet build")]
    public void Rewrite_BehindAnyOtherWord_IsReturnedUnchanged(string command)
    {
        DotnetCommandRewriter.Rewrite(command).Should().BeSameAs(command);
    }

    [Theory]
    // A text filter written for MSBuild's lines: dtk's `(12,5) CS0029: …` has neither "error" nor "warn".
    [InlineData("dotnet build 2>&1 | grep -E \"error|warn\"")]
    [InlineData("dotnet build | grep error")]
    [InlineData("dotnet build |& grep error")]
    [InlineData("dotnet build | /usr/bin/grep error")]
    [InlineData("dotnet build 2>&1 | egrep 'error|warning'")]
    [InlineData("dotnet test 2>&1 | rg Failed")]
    [InlineData("dotnet build 2>&1 | awk '/error/'")]
    [InlineData("dotnet build | sed -n '/Build succeeded/,$p'")]
    [InlineData("dotnet build | cut -c1-200")]
    [InlineData("dotnet test --filter \"A|B\" --logger \"console;verbosity=normal\" | grep Passed")]
    [InlineData("cd src && dotnet build 2>&1 | grep -c warning")]
    // A file read afterwards: `dotnet build > x.log; grep … x.log`.
    [InlineData("dotnet build > build.log")]
    [InlineData("dotnet build >build.log 2>&1; grep error build.log")]
    [InlineData("dotnet build >> build.log 2>&1")]
    [InlineData("dotnet build 2>&1 >build.log")]
    [InlineData("dotnet build 1>build.log")]
    [InlineData("dotnet build &> build.log")]
    [InlineData("dotnet build >& build.log")]
    public void Rewrite_OutputForATextFilterOrAFile_IsReturnedUnchanged(string command)
    {
        // What the command does with dotnet's output expects MSBuild's own text: it gets it.
        DotnetCommandRewriter.Rewrite(command).Should().BeSameAs(command);
    }

    [Theory]
    // The verdict is dtk's last line, so its tail and head keep it.
    [InlineData("dotnet build | tail -5", "dtk dotnet build | tail -5")]
    [InlineData("dotnet build 2>&1 | tail -30", "dtk dotnet build 2>&1 | tail -30")]
    [InlineData("dotnet test 2>&1 | head -50", "dtk dotnet test 2>&1 | head -50")]
    [InlineData("dotnet build | tail -5 | grep error", "dtk dotnet build | tail -5 | grep error")]
    [InlineData("dotnet build 2>&1 | tee build.log | tail", "dtk dotnet build 2>&1 | tee build.log | tail")]
    // Only standard error goes elsewhere, or standard output joins it.
    [InlineData("dotnet build 2> err.log", "dtk dotnet build 2> err.log")]
    [InlineData("dotnet build 2>&1", "dtk dotnet build 2>&1")]
    [InlineData("dotnet build 1>&2", "dtk dotnet build 1>&2")]
    [InlineData("dotnet build >&2", "dtk dotnet build >&2")]
    // Not a pipe: what follows runs after it, or beside it.
    [InlineData("dotnet build || echo failed | grep x", "dtk dotnet build || echo failed | grep x")]
    [InlineData("dotnet build && dotnet test | grep Failed", "dtk dotnet build && dotnet test | grep Failed")]
    [InlineData("dotnet build | grep error; dotnet test", "dotnet build | grep error; dtk dotnet test")]
    [InlineData("dotnet build; echo x | grep y", "dtk dotnet build; echo x | grep y")]
    [InlineData("dotnet build & wait", "dtk dotnet build & wait")]
    [InlineData("out=\"$(dotnet build 2>&1)\"; echo \"$out\" | grep x", "out=\"$(dtk dotnet build 2>&1)\"; echo \"$out\" | grep x")]
    [InlineData("dotnet test --filter \"A|B\" | tail", "dtk dotnet test --filter \"A|B\" | tail")]
    [InlineData("dotnet test --filter 'x>1' | tail", "dtk dotnet test --filter 'x>1' | tail")]
    public void Rewrite_OutputForDtksReader_IsPrefixedWithDtk(string command, string expected)
    {
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
    [InlineData("dotnet list ISDM.slnx package --vulnerable", true)]
    [InlineData("dotnet list ISDM.slnx reference", false)]
    [InlineData("dotnet publish -c Release -p:PublishProfile=Production", false)]
    [InlineData("dotnet publish", false)]
    [InlineData("dotnet pack -o artifacts", false)]
    [InlineData("dotnet tool restore", false)]
    [InlineData("dotnet ef migrations list", false)]
    [InlineData("dotnet ef database update", false)]
    [InlineData("dotnet stryker", false)]
    [InlineData("dotnet jb cleanupcode App.slnx", false)]
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
    // The shell removes a backslash-newline, even inside double quotes: each of these reaches dtk as `--tee`.
    [InlineData("dotnet test --te\\\ne ~/.bashrc")]
    [InlineData("dotnet test --t\\\nee=x")]
    [InlineData("dotnet test -\\\n-tee x")]
    [InlineData("dotnet test \"--te\\\ne\" x")]
    // The same continuation turns an approvable `build` into `dotnet build-server shutdown`.
    [InlineData("dotnet build\\\n-server shutdown")]
    // Fail-closed: any newline or carriage return is refused, quoted or not.
    [InlineData("dotnet test --filter \"A\nB\"")]
    [InlineData("dotnet test --filter 'A\nB'")]
    [InlineData("dotnet build\r")]
    public void IsAutoApprovable_IsFalse_WhenTheShellExpandsTheCommand(string command)
    {
        // Expansion can assemble a `--tee` the text does not show, and so can a line continuation, so neither
        // an expanded command nor one with a newline is auto-approved.
        DotnetCommandRewriter.IsSimpleCommand(command).Should().BeTrue("only the expansion or newline rule refuses it");
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
    // A word-initial `~` is expanded but not refused: it yields a home directory, never `--tee`.
    [InlineData("dotnet build ~/src/App.csproj")]
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
