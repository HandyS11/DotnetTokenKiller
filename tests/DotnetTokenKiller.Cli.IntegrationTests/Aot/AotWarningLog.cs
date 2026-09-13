using System.Text.RegularExpressions;

namespace DotnetTokenKiller.Cli.IntegrationTests.Aot;

/// <summary>
/// Reads a Native AOT pack or publish log and reports every trim or AOT diagnostic outside the one
/// accepted exception: IL2104, IL3053 and IL3000, as warnings, from Spectre.Console.Cli.
/// </summary>
internal static partial class AotWarningLog
{
    private static readonly string[] AllowedCodes = ["IL2104", "IL3053", "IL3000"];

    /// <summary>Lists what is wrong with <paramref name="log"/>, or nothing when it holds exactly the accepted warnings.</summary>
    /// <param name="log">The whole text of an MSBuild log from <c>dotnet pack -r &lt;rid&gt;</c> or <c>dotnet publish</c>.</param>
    /// <returns>One line per unexpected diagnostic, and one per accepted code that never appeared.</returns>
    internal static List<string> FindProblems(string log)
    {
        var diagnostics = log.Split('\n')
            .Select(line => DiagnosticRegex().Match(line))
            .Where(match => match.Success)
            .Select(match => new Diagnostic(
                match.Groups["origin"].Value.Trim(),
                match.Groups["level"].Value,
                match.Groups["code"].Value,
                match.Groups["message"].Value.Trim()))
            .Distinct()
            .ToList();

        var problems = diagnostics
            .Where(d => d.Level != "warning"
                        || !AllowedCodes.Contains(d.Code, StringComparer.Ordinal)
                        || !d.Origin.Contains("Spectre.Console.Cli", StringComparison.OrdinalIgnoreCase))
            .Select(d => $"unexpected {d.Level} {d.Code} from {d.Origin}: {d.Message}")
            .ToList();

        // A missing accepted warning means the native compile did not run, or Spectre.Console.Cli changed
        // and the exception has to be re-verified; either way the log proves nothing.
        problems.AddRange(AllowedCodes
            .Where(code => !diagnostics.Exists(d => d.Code == code))
            .Select(code => $"expected warning {code} from Spectre.Console.Cli, but the log has none"));

        return problems;
    }

    // "<origin>: warning IL2104: <message> [<project>]", where <origin> is a file, a file(line) or "ILC".
    [GeneratedRegex(@"^\s*(?<origin>.*?)\s*:\s*(?:Trim analysis |AOT analysis )?(?<level>warning|error) (?<code>IL\d{4})\s*:\s*(?<message>.*?)(?:\s*\[[^\]]*\])?\s*$")]
    private static partial Regex DiagnosticRegex();

    private sealed record Diagnostic(string Origin, string Level, string Code, string Message);
}
