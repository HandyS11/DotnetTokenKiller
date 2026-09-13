namespace DotnetTokenKiller.Cli.IntegrationTests.Helpers;

/// <summary>Decides how integration tests start dtk: the JIT build through <c>dotnet</c>, or a given binary.</summary>
internal static class DtkLauncher
{
    /// <summary>The environment variable naming a dtk executable to test instead of the JIT build.</summary>
    internal const string TestBinaryVariable = "DTK_TEST_BINARY";

    /// <summary>Resolves the executable to start and the arguments that precede dtk's own.</summary>
    /// <param name="testBinary">The value of <see cref="TestBinaryVariable"/>, or <see langword="null"/>.</param>
    /// <param name="dllPath">The JIT build's <c>dtk.dll</c>.</param>
    /// <returns><c>dotnet</c> and <paramref name="dllPath"/> when no binary is given; the binary alone otherwise.</returns>
    internal static (string Executable, string[] PrefixArguments) Resolve(string? testBinary, string dllPath) =>
        string.IsNullOrWhiteSpace(testBinary) ? ("dotnet", [dllPath]) : (testBinary.Trim(), []);
}
