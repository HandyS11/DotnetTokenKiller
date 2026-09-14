using System.Buffers;
using System.Globalization;
using System.Text;
using DotnetTokenKiller.Domain;

namespace DotnetTokenKiller.Application.Integration.Hooks;

/// <summary>
/// Prefixes <c>dotnet &lt;subcommand&gt;</c> invocations in a shell command with <c>dtk</c>, for the harness
/// hooks <c>dtk hook</c> answers.
/// </summary>
/// <remarks>
/// A behavior-identical port of <c>rewrite()</c> and <c>_is_simple_command()</c> from the Python hooks dtk
/// generated before <c>dtk hook</c>. A match is <c>dotnet</c>, whitespace, and a subcommand (any whitespace
/// run between a multi-token subcommand's tokens, the longest subcommand tried first, a word boundary after
/// it). It is left alone when the character before it cannot end a previous command (a path such as
/// <c>/usr/lib/dotnet/dotnet build</c>), when it sits inside quotes, or when <c>dtk</c> already runs it.
/// Python's <c>\s</c> and <c>\w</c> are Unicode-aware, which <see cref="IsPythonSpace"/> and
/// <see cref="IsPythonWord"/> reproduce.
/// </remarks>
internal static class DotnetCommandRewriter
{
    private const string Dotnet = "dotnet";
    private const string Prefix = "dtk dotnet ";

    /// <summary>Characters that may precede <c>dotnet</c> where a command starts.</summary>
    private static readonly SearchValues<char> Boundaries = SearchValues.Create(" \t;&|({`\n");

    /// <summary>Unquoted characters that chain, pipe or subshell another command.</summary>
    private static readonly SearchValues<char> Chaining = SearchValues.Create(";&|`\n()");

    /// <summary>Each subcommand's tokens, longest name first; a stable sort keeps equal lengths alphabetical.</summary>
    private static readonly string[][] SubcommandTokens =
        [.. DotnetSubcommands.Sorted.OrderByDescending(name => name.Length).Select(name => name.Split(' '))];

    /// <summary>Prefixes every qualifying <c>dotnet &lt;subcommand&gt;</c> in <paramref name="command"/> with <c>dtk</c>.</summary>
    /// <param name="command">The shell command a harness is about to run.</param>
    /// <returns>The rewritten command, or <paramref name="command"/> itself when nothing qualified.</returns>
    internal static string Rewrite(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        StringBuilder? rewritten = null;
        var copied = 0;
        var start = command.IndexOf(Dotnet, StringComparison.Ordinal);

        while (start >= 0)
        {
            if (!IsWordBefore(command, start)
                && TryMatchSubcommand(command, start + Dotnet.Length, out var subcommandStart, out var end))
            {
                if (IsCommandPosition(command, start))
                {
                    rewritten ??= new StringBuilder(command.Length + 4);
                    rewritten.Append(command, copied, start - copied)
                        .Append(Prefix)
                        .Append(command, subcommandStart, end - subcommandStart);
                    copied = end;
                }

                start = command.IndexOf(Dotnet, end, StringComparison.Ordinal);
            }
            else
            {
                start = command.IndexOf(Dotnet, start + 1, StringComparison.Ordinal);
            }
        }

        return rewritten is null ? command : rewritten.Append(command, copied, command.Length - copied).ToString();
    }

    /// <summary>
    /// Whether <paramref name="command"/> is a single invocation with no unquoted operator that could run
    /// another command beside it. Copilot CLI auto-approves only such commands.
    /// </summary>
    /// <param name="command">The original, unrewritten command.</param>
    internal static bool IsSimpleCommand(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var i = 0;
        while (i < command.Length)
        {
            var c = command[i];
            if (c == '\\')
            {
                i += 2;
                continue;
            }

            if (Chaining.Contains(c) && !IsInsideQuotes(command, i))
            {
                return false;
            }

            i++;
        }

        return true;
    }

    private static bool TryMatchSubcommand(string command, int position, out int subcommandStart, out int end)
    {
        subcommandStart = SkipSpaces(command, position);
        end = subcommandStart;
        if (subcommandStart == position)
        {
            return false;
        }

        foreach (var tokens in SubcommandTokens)
        {
            if (TryMatchTokens(command, subcommandStart, tokens, out end) && !IsWordAt(command, end))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryMatchTokens(string command, int position, string[] tokens, out int end)
    {
        end = position;
        for (var i = 0; i < tokens.Length; i++)
        {
            if (i > 0)
            {
                var afterSpaces = SkipSpaces(command, end);
                if (afterSpaces == end)
                {
                    return false;
                }

                end = afterSpaces;
            }

            if (!command.AsSpan(end).StartsWith(tokens[i], StringComparison.Ordinal))
            {
                return false;
            }

            end += tokens[i].Length;
        }

        return true;
    }

    /// <summary>The boundary, quote and already-prefixed checks of the Python <c>_replace</c>.</summary>
    /// <param name="command">The full command being scanned.</param>
    /// <param name="start">The index of the <c>dotnet</c> token being considered.</param>
    private static bool IsCommandPosition(string command, int start)
    {
        if (start > 0 && !Boundaries.Contains(command[start - 1]))
        {
            return false;
        }

        if (IsInsideQuotes(command, start))
        {
            return false;
        }

        var preceding = command.AsSpan(0, start);
        var length = preceding.Length;
        while (length > 0 && IsPythonSpace(preceding[length - 1]))
        {
            length--;
        }

        preceding = preceding[..length];
        var word = preceding[(preceding.LastIndexOfAny(Boundaries) + 1)..];
        var program = word[(word.LastIndexOfAny('/', '\\') + 1)..];
        return program is not ("dtk" or "dtk.exe");
    }

    /// <summary>Whether <paramref name="index"/> falls inside a quoted region, honoring backslash escapes outside single quotes.</summary>
    /// <param name="command">The full command being scanned.</param>
    /// <param name="index">The index to test.</param>
    private static bool IsInsideQuotes(string command, int index)
    {
        var inSingle = false;
        var inDouble = false;
        var i = 0;
        while (i < index)
        {
            var c = command[i];
            if (c == '\\' && !inSingle)
            {
                i += 2;
                continue;
            }

            if (c == '\'' && !inDouble)
            {
                inSingle = !inSingle;
            }
            else if (c == '"' && !inSingle)
            {
                inDouble = !inDouble;
            }

            i++;
        }

        return inSingle || inDouble;
    }

    private static int SkipSpaces(string command, int index)
    {
        while (index < command.Length && IsPythonSpace(command[index]))
        {
            index++;
        }

        return index;
    }

    private static bool IsWordBefore(string command, int index) =>
        index > 0
        && Rune.DecodeLastFromUtf16(command.AsSpan(0, index), out var rune, out _) == OperationStatus.Done
        && IsPythonWord(rune);

    private static bool IsWordAt(string command, int index) =>
        index < command.Length
        && Rune.DecodeFromUtf16(command.AsSpan(index), out var rune, out _) == OperationStatus.Done
        && IsPythonWord(rune);

    /// <summary>Python's <c>str.isspace()</c>, which also counts the separators U+001C–U+001F.</summary>
    /// <param name="c">The character to test.</param>
    private static bool IsPythonSpace(char c) => char.IsWhiteSpace(c) || c is >= '\u001c' and <= '\u001f';

    /// <summary>Python's <c>\w</c>: an underscore, a letter, or any numeric character, including superscripts.</summary>
    /// <param name="rune">The code point to test.</param>
    private static bool IsPythonWord(Rune rune) =>
        rune.Value == '_'
        || Rune.GetUnicodeCategory(rune) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber;
}
