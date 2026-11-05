using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Infrastructure.Execution;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Infrastructure.Tests.Execution;

public sealed class GlobalJsonTestRunnerDetectorTests : IDisposable
{
    private const string MtpGlobalJson = """{"test":{"runner":"Microsoft.Testing.Platform"}}""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dtk-runner-test-{Guid.NewGuid():N}");
    private readonly GlobalJsonTestRunnerDetector _sut = new();

    public GlobalJsonTestRunnerDetectorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void Detect_NoGlobalJson_IsVsTest()
    {
        _sut.Detect(_root).Should().Be(TestRunner.VsTest);
    }

    [Fact]
    public void Detect_MtpRunner_IsMtp()
    {
        // The SDK reads the nearest global.json above the working directory, not only the one beside it.
        File.WriteAllText(Path.Combine(_root, "global.json"), MtpGlobalJson);
        var nested = Directory.CreateDirectory(Path.Combine(_root, "tests", "Alpha.Tests")).FullName;

        _sut.Detect(nested).Should().Be(TestRunner.MicrosoftTestingPlatform);
    }

    [Fact]
    public void Detect_RunnerValueIgnoresCase()
    {
        // SDK 10.0.403 runs Microsoft.Testing.Platform for "microsoft.testing.platform" too.
        File.WriteAllText(Path.Combine(_root, "global.json"), """{"test":{"runner":"microsoft.testing.platform"}}""");

        _sut.Detect(_root).Should().Be(TestRunner.MicrosoftTestingPlatform);
    }

    [Fact]
    public void Detect_NearestGlobalJsonWins()
    {
        // The search stops at the first global.json, as the SDK's does: a parent's runner does not apply.
        File.WriteAllText(Path.Combine(_root, "global.json"), MtpGlobalJson);
        var child = Directory.CreateDirectory(Path.Combine(_root, "child")).FullName;
        File.WriteAllText(Path.Combine(child, "global.json"), """{"sdk":{"version":"10.0.100"}}""");

        _sut.Detect(child).Should().Be(TestRunner.VsTest);
    }

    [Theory]
    [InlineData("""{"test":{"runner":"VSTest"}}""")]
    [InlineData("""{"sdk":{"version":"10.0.100"}}""")]
    [InlineData("""{"test":{}}""")]
    [InlineData("{}")]
    // The SDK reads "test" and "runner" case-sensitively: SDK 10.0.403 runs VSTest for this one.
    [InlineData("""{"Test":{"Runner":"Microsoft.Testing.Platform"}}""")]
    public void Detect_OtherRunner_IsVsTest(string globalJson)
    {
        File.WriteAllText(Path.Combine(_root, "global.json"), globalJson);

        _sut.Detect(_root).Should().Be(TestRunner.VsTest);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("not json")]
    [InlineData("""{"test":{"runner":"Microsoft.Testing.Platform"}""")]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("""{"test":"Microsoft.Testing.Platform"}""")]
    [InlineData("""{"test":{"runner":42}}""")]
    public void Detect_Malformed_IsUnknown(string globalJson)
    {
        File.WriteAllText(Path.Combine(_root, "global.json"), globalJson);

        _sut.Detect(_root).Should().Be(TestRunner.Unknown);
    }

    [Fact]
    public void Detect_Unreadable_IsUnknown()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // POSIX permission bits are not meaningful on Windows
        }

        var path = Path.Combine(_root, "global.json");
        File.WriteAllText(path, MtpGlobalJson);
        File.SetUnixFileMode(path, UnixFileMode.None);
        if (CanRead(path))
        {
            return; // root reads a file whatever its mode
        }

        _sut.Detect(_root).Should().Be(TestRunner.Unknown);
    }

    [Fact]
    public void Detect_CommentsAndTrailingCommas_Parse()
    {
        File.WriteAllText(Path.Combine(_root, "global.json"), """
            {
              // the runner dotnet test uses
              "test": { "runner": "Microsoft.Testing.Platform", },
              /* the SDK */
              "sdk": { "version": "10.0.100", },
            }
            """);

        _sut.Detect(_root).Should().Be(TestRunner.MicrosoftTestingPlatform);
    }

    private static bool CanRead(string path)
    {
        try
        {
            File.ReadAllBytes(path);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
