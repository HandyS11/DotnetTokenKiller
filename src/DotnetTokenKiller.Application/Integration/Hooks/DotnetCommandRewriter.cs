using System.Buffers;
using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using DotnetTokenKiller.Domain;
using DotnetTokenKiller.Domain.Execution;

namespace DotnetTokenKiller.Application.Integration.Hooks;

/// <summary>
/// Prefixes <c>dotnet &lt;subcommand&gt;</c> invocations in a shell command with <c>dtk</c>, for the harness
/// hooks <c>dtk hook</c> answers.
/// </summary>
/// <remarks>
/// <para>
/// A match is <c>dotnet</c>, blanks (spaces or tabs), and a subcommand (blanks between a multi-token
/// subcommand's tokens, the longest subcommand tried first, a word boundary after it). It is left alone when
/// the character before it cannot end a previous command (a path such as <c>/usr/lib/dotnet/dotnet build</c>),
/// when <c>dtk</c> already runs it, when it asks for dotnet's raw output (<c>rtk proxy dotnet …</c>,
/// <c>command dotnet …</c>, <c>DTK_RAW=1 dotnet …</c>), when it is not shell code at all, and when a word before it
/// is anything but an assignment, a shell keyword or a launcher known to run it here with the same <c>PATH</c>
/// (<see cref="RunsHereWithTheSamePath"/>): <c>$D dotnet build</c>, <c>echo dotnet build</c>,
/// <c>ssh host dotnet build</c> and <c>sudo dotnet build</c> stay as they are. So does one whose output goes to a text
/// filter or a file (<see cref="SendsOutputToATextFilterOrFile"/>): <c>dotnet build 2&gt;&amp;1 | grep -E "error|warn"</c>
/// and <c>dotnet build &gt; x.log</c> expect MSBuild's own lines.
/// <see cref="IsPythonWord"/> keeps the Unicode-aware <c>\w</c> of the Python hooks dtk generated before
/// <c>dtk hook</c>, which this class started as a port of.
/// </para>
/// <para>
/// One left-to-right pass of <see cref="ShellScanner"/> decides what is shell code. It understands backslash
/// escapes, single quotes, ANSI-C <c>$'…'</c> and locale <c>$"…"</c> strings, <c>$$</c>, double quotes,
/// <c>$(…)</c> and backtick command substitutions (each starting a fresh command, even inside double quotes),
/// parentheses, <c>#</c> comments that start a word, and here-document bodies (<c>&lt;&lt;</c> and
/// <c>&lt;&lt;-</c>, with quoted or unquoted delimiters), which are data and never rewritten. <c>${…}</c>
/// expansions, <c>case</c> patterns and arithmetic are not parsed. Where they mislead the scanner, it can
/// leave a <c>dotnet</c> command unrewritten or rewrite text that is really data (quotes nested inside
/// <c>"${…}"</c> are read as closing the string).
/// </para>
/// <para>
/// <see cref="IsSimpleCommand"/> does not rely on that precision. It decides an auto-approval, so it accepts
/// only a command whose first word is <c>dotnet</c> and in which the scanner met nothing but plain words,
/// quoted text and blanks. Comments, ANSI-C and locale strings, <c>${…}</c>, <c>$[…]</c>, <c>!</c> (history
/// expansion, in shells that enable it), substitutions, here-documents, redirections, operators and
/// unterminated quotes all make a command not simple, so a mis-scan can cost a rewrite but never an approval.
/// </para>
/// </remarks>
internal static class DotnetCommandRewriter
{
    private const string Dotnet = "dotnet";
    private const string Prefix = "dtk dotnet ";

    /// <summary>dtk's option that writes a file, which <see cref="IsAutoApprovable"/> never approves.</summary>
    private const string TeeOption = "--tee";

    /// <summary>The shortest prefix a glob could complete to <see cref="TeeOption"/> from (<c>--t*</c>).</summary>
    private const string TeeGlobPrefix = "--t";

    /// <summary>The quoting characters the shell removes from a word before the program sees it.</summary>
    private static readonly SearchValues<char> QuotingCharacters = SearchValues.Create("'\"\\");

    /// <summary>The characters that make a word a glob pattern the shell may replace with file names.</summary>
    private static readonly SearchValues<char> GlobCharacters = SearchValues.Create("*?[");

    /// <summary>
    /// Characters a project or solution between a subcommand's words (<c>dotnet list ISDM.slnx package</c>) may not
    /// hold: quoting, expansion, operators and line breaks, which a plain path does not need. Such a word is not
    /// read as one, and the command stays as it is.
    /// </summary>
    private static readonly SearchValues<char> NotInTarget = SearchValues.Create("\"'\\`$;&|()<>{}*?[]!#\n\r");

    /// <summary>The blanks the shell splits words on.</summary>
    private static readonly char[] WordSeparators = [' ', '\t', '\n'];

    /// <summary>The line breaks <see cref="IsAutoApprovable"/> refuses wherever they stand.</summary>
    private static readonly SearchValues<char> LineBreaks = SearchValues.Create("\n\r");

    /// <summary>Characters that may precede <c>dotnet</c> where a command starts.</summary>
    private static readonly SearchValues<char> Boundaries = SearchValues.Create(" \t;&|({`\n");

    /// <summary><see cref="Boundaries"/> without the blanks: the characters that end the command before a word.</summary>
    private static readonly SearchValues<char> CommandSeparators = SearchValues.Create(";&|({`\n");

    /// <summary>
    /// The programs that pick lines out of text by what they say: written after <c>dotnet build |</c>, they look for
    /// MSBuild's <c>error</c>, <c>warning</c> or <c>Build succeeded</c>, which dtk's lines do not carry.
    /// </summary>
    private static readonly FrozenSet<string> TextFilters =
        new[] { "grep", "egrep", "fgrep", "rg", "ag", "ack", "awk", "gawk", "mawk", "nawk", "sed", "cut" }
            .ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The reserved words that may stand before a command, which they run in this shell.</summary>
    private static readonly FrozenSet<string> ShellKeywords =
        new[] { "!", "if", "then", "else", "elif", "do", "while", "until" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The options of <c>timeout</c> that take a value in the next word.</summary>
    private static readonly FrozenSet<string> TimeoutValueOptions =
        new[] { "-s", "-k", "--signal", "--kill-after" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>The options of <c>timeout</c> that take none, or carry theirs after <c>=</c>.</summary>
    private static readonly FrozenSet<string> TimeoutFlags =
        new[] { "--foreground", "--preserve-status", "-v", "--verbose" }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Unquoted characters that chain, pipe, subshell or redirect, none of which a simple command has.</summary>
    private static readonly SearchValues<char> NotSimple = SearchValues.Create(";&|`\n()<>");

    /// <summary>The characters of a shell variable's name.</summary>
    private static readonly SearchValues<char> VariableNameCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_");

    /// <summary>Characters that make an assignment's value something other than its literal text.</summary>
    private static readonly SearchValues<char> ShellValueCharacters = SearchValues.Create("\"'\\$`");

    /// <summary>Each subcommand's tokens, longest name first; a stable sort keeps equal lengths alphabetical.</summary>
    private static readonly string[][] SubcommandTokens =
        [.. DotnetSubcommands.Sorted.OrderByDescending(name => name.Length).Select(name => name.Split(' '))];

    /// <summary>
    /// The subcommands <see cref="IsAutoApprovable"/> accepts: exactly the ones dtk rewrote before it learned
    /// <c>publish</c> and <c>pack</c>. Deliberately spelled out rather than derived from
    /// <see cref="DotnetSubcommands.Ordered"/>, so a subcommand added there gains a rewrite but never, silently,
    /// an auto-approval; widening this list is a decision of its own.
    /// </summary>
    internal static readonly IReadOnlyList<string> AutoApprovableSubcommands =
    [
        DotnetSubcommands.Build, DotnetSubcommands.Test, DotnetSubcommands.Restore, DotnetSubcommands.Clean,
        DotnetSubcommands.Format, DotnetSubcommands.ListPackage
    ];

    /// <summary><see cref="AutoApprovableSubcommands"/>' tokens, longest name first.</summary>
    private static readonly string[][] AutoApprovableTokens =
        [.. AutoApprovableSubcommands.OrderByDescending(name => name.Length).Select(name => name.Split(' '))];

    /// <summary>Prefixes every qualifying <c>dotnet &lt;subcommand&gt;</c> in <paramref name="command"/> with <c>dtk</c>.</summary>
    /// <param name="command">The shell command a harness is about to run.</param>
    /// <returns>The rewritten command, or <paramref name="command"/> itself when nothing qualified.</returns>
    internal static string Rewrite(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!command.Contains(Dotnet, StringComparison.Ordinal))
        {
            return command;
        }

        StringBuilder? rewritten = null;
        var copied = 0;
        var scanner = new ShellScanner(command);

        while (scanner.MoveNext(out var start, out var token))
        {
            if (token == ShellToken.Code
                && command[start] == 'd'
                && command.AsSpan(start).StartsWith(Dotnet, StringComparison.Ordinal)
                && !IsWordBefore(command, start)
                && TryMatchSubcommand(command, start + Dotnet.Length, out var subcommandStart, out var end)
                && IsCommandPosition(command, start)
                && !SendsOutputToATextFilterOrFile(command, end))
            {
                rewritten ??= new StringBuilder(command.Length + 4);
                rewritten.Append(command, copied, start - copied)
                    .Append(Prefix)
                    .Append(command, subcommandStart, end - subcommandStart);
                copied = end;
            }
        }

        return rewritten is null ? command : rewritten.Append(command, copied, command.Length - copied).ToString();
    }

    /// <summary>
    /// Whether <paramref name="command"/> is a single <c>dotnet</c> invocation with nothing that could run
    /// another command beside it or write elsewhere: its first word is <c>dotnet</c>, and it has no unquoted
    /// operator or redirection, no command substitution (even inside double quotes), no here-document, comment,
    /// ANSI-C or locale string, <c>${…}</c> or <c>$[…]</c> expansion or <c>!</c> outside single quotes, and no
    /// unterminated quote. Copilot CLI auto-approves
    /// only such commands, so anything the scanner cannot vouch for is not simple.
    /// </summary>
    /// <param name="command">The original, unrewritten command.</param>
    internal static bool IsSimpleCommand(string command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var trimmed = command.AsSpan().TrimStart(" \t");
        if (!trimmed.StartsWith(Dotnet, StringComparison.Ordinal)
            || trimmed.Length == Dotnet.Length
            || trimmed[Dotnet.Length] is not (' ' or '\t'))
        {
            return false;
        }

        var scanner = new ShellScanner(command);
        while (scanner.MoveNext(out var index, out var token))
        {
            if (token != ShellToken.Code || NotSimple.Contains(command[index]))
            {
                return false;
            }
        }

        return scanner.IsTerminated();
    }

    /// <summary>
    /// Whether Copilot CLI may run <paramref name="command"/>'s rewrite without asking: it is
    /// <see cref="IsSimpleCommand">simple</see>, its subcommand is one of <see cref="AutoApprovableSubcommands"/>,
    /// it holds no newline or carriage return anywhere (quoted, escaped or not), it has no brace, parameter or glob
    /// expansion (<see cref="ShellExpands"/>), and quote removal could not hand dtk a <c>--tee</c> option from it
    /// (<see cref="MayPassTeeOption"/>). A word-initial <c>~</c> is not refused: tilde expansion yields a home
    /// directory, never <c>--tee</c>, short of control over <c>HOME</c>.
    /// <c>dotnet publish</c> and <c>dotnet pack</c> are still rewritten, but they write artifacts (and a publish
    /// profile can deploy), so they are left to the user's own approval.
    /// </summary>
    /// <remarks>
    /// The newline rule closes line continuation: the shell removes a backslash-newline before the words are split,
    /// even inside double quotes, so <c>--te\</c>, a newline and <c>e</c> reach dtk as <c>--tee</c>, and
    /// <c>dotnet build\</c>, a newline and <c>-server shutdown</c> run <c>dotnet build-server shutdown</c>: words no
    /// rule that reads the text as written can see. Refusing every newline also covers quoted ones and multi-line
    /// commands without deciding which ones the shell would join.
    /// </remarks>
    /// <param name="command">The original, unrewritten command.</param>
    internal static bool IsAutoApprovable(string command)
    {
        if (!IsSimpleCommand(command) || command.AsSpan().ContainsAny(LineBreaks) || MayPassTeeOption(command))
        {
            return false;
        }

        var position = command.AsSpan().IndexOf(Dotnet, StringComparison.Ordinal) + Dotnet.Length;
        var subcommandStart = SkipSpaces(command, position);
        foreach (var tokens in AutoApprovableTokens)
        {
            if (TryMatchSubcommandTokens(command, subcommandStart, tokens, out var end) && !IsWordAt(command, end))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the shell might hand dtk a <c>--tee</c> option, in any form (<c>--tee x</c>, <c>--tee=x</c>,
    /// <c>--tee:x</c>), from <paramref name="command"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>--tee &lt;path&gt;</c> writes dtk's copy of the raw output wherever its value points, so
    /// <c>dotnet test --tee ~/.bashrc</c> is the file write <see cref="IsSimpleCommand"/> refuses as
    /// <c>dotnet test &gt; ~/.bashrc</c>.
    /// </para>
    /// <para>
    /// Auto-approval never covers a command with a brace, parameter or glob expansion, because expansion can
    /// assemble a <c>--tee</c> the text does not show: <c>--te$9e</c> is <c>--tee</c> (an unset parameter expands
    /// to nothing), <c>{--te,x}e</c> is <c>--tee xe</c>, and <c>--te[e]</c> matches a file named <c>--tee</c>. See
    /// <see cref="ShellExpands"/> for what counts. A word-initial <c>~</c> is the one expansion left in: it yields a
    /// home directory, never <c>--tee</c>, unless the caller controls <c>HOME</c>. Line continuation, which can join
    /// <c>--te</c> and <c>e</c> across a backslash-newline, is refused before this runs
    /// (<see cref="IsAutoApprovable"/> refuses every newline).
    /// </para>
    /// <para>
    /// Nor does it cover a command that contains <c>--tee</c> once quote removal is done, with every <c>'</c>,
    /// <c>"</c> and <c>\</c> removed (<c>--"tee"</c>, <c>--t\ee</c>, <c>--te''e</c>), or in which a word of that
    /// quote-stripped text starts with <c>--t</c> and holds <c>*</c>, <c>?</c> or <c>[</c>.
    /// </para>
    /// <para>
    /// None of these parses the command, and each matches far more than an option (a quoted <c>--filter "A{B}"</c>,
    /// an unquoted <c>Foo*</c>, a test named <c>--tee</c>): a false match costs an approval prompt, never an approval.
    /// </para>
    /// </remarks>
    /// <param name="command">A <see cref="IsSimpleCommand">simple</see> command.</param>
    private static bool MayPassTeeOption(string command)
    {
        if (ShellExpands(command))
        {
            return true;
        }

        // Every quoting character goes, wherever it stands: no scan decides which ones the shell would keep.
        var unquoted = string.Concat(command.Where(character => !QuotingCharacters.Contains(character)));
        return unquoted.Contains(TeeOption, StringComparison.Ordinal)
               || unquoted.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries).Any(word =>
                   word.StartsWith(TeeGlobPrefix, StringComparison.Ordinal) && word.AsSpan().ContainsAny(GlobCharacters));
    }

    /// <summary>
    /// Whether <paramref name="command"/> has a brace, parameter or glob expansion: a <c>{</c> anywhere (brace
    /// expansion), a <c>$</c> anywhere outside single quotes (a parameter; double-quoted and backslash-escaped ones
    /// included, fail-closed), or a <c>*</c>, <c>?</c> or <c>[</c> outside quotes and unescaped (a glob).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not every expansion counts. Tilde expansion is left alone: a word-initial <c>~</c> yields a home directory,
    /// which cannot spell <c>--tee</c> without control over <c>HOME</c>. Line continuation is not this method's job
    /// either: <see cref="IsAutoApprovable"/> refuses every newline before this runs.
    /// </para>
    /// <para>
    /// A walk of its own rather than <see cref="ShellScanner"/>'s, which reports no character inside double quotes,
    /// where a <c>$</c> still expands. A simple command has no <c>$'…'</c>, <c>$"…"</c>, here-document or
    /// substitution, so its quotes and backslashes are all this has to follow.
    /// </para>
    /// </remarks>
    /// <param name="command">A <see cref="IsSimpleCommand">simple</see> command.</param>
    private static bool ShellExpands(string command)
    {
        if (command.Contains('{', StringComparison.Ordinal))
        {
            return true;
        }

        var inDoubleQuotes = false;
        var position = 0;
        while (position < command.Length)
        {
            switch (command[position++])
            {
                case '$':
                    return true;
                case '\\' when position < command.Length && command[position] != '$':
                    // The escaped character is literal. An escaped `$` is not skipped, so it is refused like any other.
                    position++;
                    break;
                case '"':
                    inDoubleQuotes = !inDoubleQuotes;
                    break;
                case '\'' when !inDoubleQuotes:
                    // Nothing inside single quotes expands. Unterminated, which a simple command never is: refused.
                    var close = command.IndexOf('\'', position);
                    if (close < 0)
                    {
                        return true;
                    }

                    position = close + 1;
                    break;
                case '*' or '?' or '[' when !inDoubleQuotes:
                    return true;
            }
        }

        return false;
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
            if (TryMatchSubcommandTokens(command, subcommandStart, tokens, out end) && !IsWordAt(command, end))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Matches a subcommand's words at <paramref name="position"/>: contiguous, or, for one of
    /// <see cref="DotnetSubcommands.TargetBetweenWords"/>, with the project or solution dotnet takes between them
    /// (<c>list ISDM.slnx package</c>).
    /// </summary>
    /// <param name="command">The full command being scanned.</param>
    /// <param name="position">Where the subcommand starts.</param>
    /// <param name="tokens">The subcommand's words.</param>
    /// <param name="end">Where the match ends.</param>
    private static bool TryMatchSubcommandTokens(string command, int position, string[] tokens, out int end) =>
        TryMatchTokens(command, position, tokens, out end)
        || (tokens.Length > 1
            && DotnetSubcommands.TargetBetweenWords.Contains(string.Join(' ', tokens))
            && TryMatchTokensAroundTarget(command, position, tokens, out end));

    /// <summary>
    /// Matches <paramref name="tokens"/>' first word, blanks, a plain word that is not an option (no character of
    /// <see cref="NotInTarget"/>), blanks, then the rest of its words.
    /// </summary>
    /// <param name="command">The full command being scanned.</param>
    /// <param name="position">Where the subcommand starts.</param>
    /// <param name="tokens">The subcommand's words, at least two.</param>
    /// <param name="end">Where the match ends.</param>
    private static bool TryMatchTokensAroundTarget(string command, int position, string[] tokens, out int end)
    {
        if (!TryMatchTokens(command, position, tokens[..1], out end))
        {
            return false;
        }

        var targetStart = SkipSpaces(command, end);
        var targetEnd = targetStart;
        while (targetEnd < command.Length && !IsBlank(command[targetEnd]) && !NotInTarget.Contains(command[targetEnd]))
        {
            targetEnd++;
        }

        var restStart = SkipSpaces(command, targetEnd);
        return targetStart > end
               && targetEnd > targetStart
               && command[targetStart] != '-'
               && restStart > targetEnd
               && TryMatchTokens(command, restStart, tokens[1..], out end);
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

    /// <summary>
    /// Whether the <c>dotnet</c> at <paramref name="start"/> follows a command boundary, is not run by <c>dtk</c>,
    /// and is not a request for dotnet's raw output: run by <c>rtk proxy</c> or the <c>command</c> builtin, or
    /// directly behind a <c>DTK_RAW=</c> assignment that turns raw output on.
    /// </summary>
    /// <param name="command">The full command being scanned.</param>
    /// <param name="start">The index of the <c>dotnet</c> token being considered.</param>
    private static bool IsCommandPosition(string command, int start)
    {
        if (start > 0 && !Boundaries.Contains(command[start - 1]))
        {
            return false;
        }

        var word = LastWord(command.AsSpan(0, start), out var beforeWord);
        var program = ProgramName(word);
        if (program is "dtk" or "dtk.exe" or "command" || IsRawOutputAssignment(word))
        {
            return false;
        }

        return (word is not "proxy" || ProgramName(LastWord(beforeWord, out _)) is not ("rtk" or "rtk.exe"))
               && RunsHereWithTheSamePath(command, start);
    }

    /// <summary>
    /// Whether the <c>dotnet</c> command whose subcommand ends at <paramref name="end"/> sends its standard output to a
    /// text filter (<c>| grep</c>, <c>|&amp; awk</c>, … <see cref="TextFilters"/>) or to a file (<c>&gt; x.log</c>,
    /// <c>&gt;&gt;</c>, <c>1&gt;</c>, <c>&amp;&gt;</c>, <c>&gt;&amp; x.log</c>), which expect MSBuild's lines. The first
    /// unquoted operator after its arguments decides: a pipe into anything else (<c>tail</c>, <c>head</c>, <c>tee</c>),
    /// <c>||</c>, <c>&amp;&amp;</c>, <c>;</c>, <c>&amp;</c>, a line break or a closing <c>)</c> does not, and neither
    /// does a redirection of another descriptor (<c>2&gt; err.log</c>, <c>2&gt;&amp;1</c>) or a copy of standard output
    /// onto one (<c>&gt;&amp;2</c>). An argument holding a command substitution ends the search, and the command is
    /// rewritten as before.
    /// </summary>
    /// <param name="command">The full command being scanned.</param>
    /// <param name="end">Where the matched subcommand ends.</param>
    private static bool SendsOutputToATextFilterOrFile(string command, int end)
    {
        var rest = command[end..];
        var scanner = new ShellScanner(rest);
        var skipThrough = -1;
        while (scanner.MoveNext(out var index, out var token))
        {
            if (token == ShellToken.Substitution)
            {
                return false;
            }

            if (index > skipThrough && token == ShellToken.Code
                                    && ReadOperator(rest, index, ref skipThrough) is { } sendsIt)
            {
                return sendsIt;
            }
        }

        return false;
    }

    /// <summary>
    /// What the unquoted character at <paramref name="index"/> says of where the output goes: <see langword="true"/>
    /// to a text filter or a file, <see langword="false"/> elsewhere or nowhere yet, or <see langword="null"/> when it
    /// says nothing (a character of an argument, a redirection of another descriptor).
    /// </summary>
    /// <param name="text">The command after the subcommand.</param>
    /// <param name="index">The index of the character.</param>
    /// <param name="skipThrough">Moved past a redirection's operator characters, which say nothing more.</param>
    private static bool? ReadOperator(string text, int index, ref int skipThrough)
    {
        var next = index + 1 < text.Length ? text[index + 1] : '\0';
        return text[index] switch
        {
            '|' => next != '|' && PipesIntoATextFilter(text, next == '&' ? index + 2 : index + 1),
            '&' => next == '>',
            '>' => RedirectsStandardOutputToAFile(text, index, out skipThrough) ? true : null,
            ';' or '\n' or ')' => false,
            _ => null
        };
    }

    /// <summary>Whether the first word after a pipe at <paramref name="start"/> names one of <see cref="TextFilters"/>.</summary>
    /// <param name="text">The command after the subcommand.</param>
    /// <param name="start">The index after the pipe.</param>
    private static bool PipesIntoATextFilter(string text, int start)
    {
        var wordStart = SkipSpaces(text, start);
        var wordEnd = wordStart;
        while (wordEnd < text.Length && !IsBlank(text[wordEnd]) && !NotSimple.Contains(text[wordEnd]))
        {
            wordEnd++;
        }

        return TextFilters.Contains(ProgramName(text.AsSpan(wordStart, wordEnd - wordStart)).ToString());
    }

    /// <summary>
    /// Whether the <c>&gt;</c> at <paramref name="index"/> sends standard output to a file: it names no descriptor or
    /// names <c>1</c>, and is not a copy onto a descriptor (<c>&gt;&amp;2</c>, <c>&gt;&amp;-</c>).
    /// </summary>
    /// <param name="text">The command after the subcommand.</param>
    /// <param name="index">The index of the <c>&gt;</c>.</param>
    /// <param name="redirectEnd">Set to the index of the redirection's last operator character.</param>
    private static bool RedirectsStandardOutputToAFile(string text, int index, out int redirectEnd)
    {
        var descriptorStart = index;
        while (descriptorStart > 0 && char.IsAsciiDigit(text[descriptorStart - 1]))
        {
            descriptorStart--;
        }

        var namesADescriptor = descriptorStart < index && (descriptorStart == 0 || IsBlank(text[descriptorStart - 1]));
        var ofStandardOutput = !namesADescriptor || text.AsSpan(descriptorStart, index - descriptorStart) is "1";

        redirectEnd = index + 1 < text.Length && text[index + 1] == '>' ? index + 1 : index;
        var copiesADescriptor = redirectEnd + 2 < text.Length
                                && text[redirectEnd + 1] == '&'
                                && (char.IsAsciiDigit(text[redirectEnd + 2]) || text[redirectEnd + 2] == '-');
        if (copiesADescriptor)
        {
            redirectEnd += 2;
        }

        return ofStandardOutput && !copiesADescriptor;
    }

    /// <summary>
    /// Whether every word between the start of the command and the <c>dotnet</c> at <paramref name="start"/> runs it
    /// here, where the <c>dtk</c> on this shell's <c>PATH</c> can: an assignment (<c>X=1</c>), a shell keyword
    /// (<c>if</c>, <c>then</c>, <c>!</c>, <c>do</c>, …), or <c>env</c>, <c>time</c>, <c>timeout</c>, <c>nice</c>,
    /// <c>nohup</c> or <c>exec</c> with the plain arguments they are known to take. Any other word, or an argument
    /// they are not known to take, leaves the command as it is: a word that expands to a program (<c>$D</c>, whose
    /// <c>$D dtk dotnet build</c> hands dtk the unknown command <c>dtk</c>), text (<c>echo</c>), another machine or
    /// user (<c>ssh</c>, <c>docker run</c>, <c>sudo</c>, which resets <c>PATH</c>), or <c>env -i</c>, which clears it.
    /// Words are split on blanks alone, so a quoted value with a blank (<c>X="a b" dotnet build</c>) also leaves it.
    /// </summary>
    /// <param name="command">The full command being scanned.</param>
    /// <param name="start">The index of the <c>dotnet</c> token being considered.</param>
    private static bool RunsHereWithTheSamePath(string command, int start)
    {
        var before = command.AsSpan(0, start);
        var words = before[(before.LastIndexOfAny(CommandSeparators) + 1)..].ToString()
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var index = 0;
        while (index < words.Length)
        {
            index = SkipRunner(words, index);
            if (index < 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The index of the word after the assignment, keyword or launcher with its arguments at <paramref name="index"/>,
    /// or <c>-1</c> when that word is none of them, or a launcher's argument is not one it is known to take.
    /// </summary>
    /// <param name="words">The words before <c>dotnet</c>.</param>
    /// <param name="index">The index of the word to read.</param>
    private static int SkipRunner(string[] words, int index)
    {
        var word = words[index];
        if (ShellKeywords.Contains(word) || IsAssignment(word))
        {
            return index + 1;
        }

        return ProgramName(word) switch
        {
            "env" => SkipEnvOptions(words, index + 1),
            "time" => index + 1 < words.Length && words[index + 1] == "-p" ? index + 2 : index + 1,
            "timeout" => SkipTimeoutArguments(words, index + 1),
            "nice" => SkipNiceOptions(words, index + 1),
            "nohup" or "exec" => index + 1,
            _ => -1
        };
    }

    /// <summary><c>env</c>'s <c>-u NAME</c> and <c>--unset=NAME</c>; any other option (<c>-i</c> clears <c>PATH</c>) fails.</summary>
    /// <param name="words">The words before <c>dotnet</c>.</param>
    /// <param name="index">The index of the word after <c>env</c>.</param>
    private static int SkipEnvOptions(string[] words, int index)
    {
        while (index < words.Length && words[index].StartsWith('-'))
        {
            if (words[index] == "-u" && index + 1 < words.Length)
            {
                index += 2;
            }
            else if (words[index].StartsWith("--unset=", StringComparison.Ordinal))
            {
                index++;
            }
            else
            {
                return -1;
            }
        }

        return index;
    }

    /// <summary><c>timeout</c>'s options, then the duration it requires (<c>600</c>, <c>1.5m</c>, <c>1h</c>).</summary>
    /// <param name="words">The words before <c>dotnet</c>.</param>
    /// <param name="index">The index of the word after <c>timeout</c>.</param>
    private static int SkipTimeoutArguments(string[] words, int index)
    {
        while (index < words.Length && words[index].StartsWith('-'))
        {
            var word = words[index];
            var name = word.Split('=', 2)[0];
            if (TimeoutValueOptions.Contains(word) && index + 1 < words.Length)
            {
                index += 2;
            }
            else if (TimeoutFlags.Contains(word) || (word.Contains('=', StringComparison.Ordinal)
                                                     && TimeoutValueOptions.Contains(name)))
            {
                index++;
            }
            else
            {
                return -1;
            }
        }

        return index < words.Length && IsDuration(words[index]) ? index + 1 : -1;
    }

    /// <summary><c>nice</c>'s <c>-n N</c>, <c>-N</c> and <c>--adjustment=N</c>.</summary>
    /// <param name="words">The words before <c>dotnet</c>.</param>
    /// <param name="index">The index of the word after <c>nice</c>.</param>
    private static int SkipNiceOptions(string[] words, int index)
    {
        if (index >= words.Length || !words[index].StartsWith('-'))
        {
            return index;
        }

        var word = words[index];
        if (word == "-n")
        {
            return index + 1 < words.Length && IsInteger(words[index + 1]) ? index + 2 : -1;
        }

        return IsInteger(word[1..]) || word.StartsWith("--adjustment=", StringComparison.Ordinal) ? index + 1 : -1;
    }

    /// <summary>Whether <paramref name="word"/> assigns a variable: a name, then <c>=</c>.</summary>
    /// <param name="word">A word of the command.</param>
    private static bool IsAssignment(string word)
    {
        var equals = word.IndexOf('=', StringComparison.Ordinal);
        return equals > 0
               && (char.IsAsciiLetter(word[0]) || word[0] == '_')
               && !word.AsSpan(0, equals).ContainsAnyExcept(VariableNameCharacters);
    }

    /// <summary>Whether <paramref name="word"/> is a whole number, with an optional sign.</summary>
    /// <param name="word">A word of the command.</param>
    private static bool IsInteger(string word) =>
        int.TryParse(word, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);

    /// <summary>Whether <paramref name="word"/> is a <c>timeout</c> duration: a number, then <c>s</c>, <c>m</c>, <c>h</c> or <c>d</c>.</summary>
    /// <param name="word">A word of the command.</param>
    private static bool IsDuration(string word)
    {
        var number = word.Length > 0 && word[^1] is 's' or 'm' or 'h' or 'd' ? word[..^1] : word;
        return number.Length > 0
               && double.TryParse(number, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _);
    }

    /// <summary>
    /// The last word of <paramref name="text"/>, ignoring trailing blanks: empty when a boundary ends it.
    /// </summary>
    /// <param name="text">The command text before the position being considered.</param>
    /// <param name="beforeWord">The text before the word.</param>
    private static ReadOnlySpan<char> LastWord(ReadOnlySpan<char> text, out ReadOnlySpan<char> beforeWord)
    {
        text = text.TrimEnd(" \t");
        var wordStart = text.LastIndexOfAny(Boundaries) + 1;
        beforeWord = text[..wordStart];
        return text[wordStart..];
    }

    /// <summary>A word's last path segment, so <c>~/.cargo/bin/rtk</c> names <c>rtk</c>.</summary>
    /// <param name="word">A word of the command.</param>
    private static ReadOnlySpan<char> ProgramName(ReadOnlySpan<char> word) =>
        word[(word.LastIndexOfAny('/', '\\') + 1)..];

    /// <summary>
    /// Whether <paramref name="word"/> is a <c>DTK_RAW=</c> assignment whose plain value turns raw output on. A
    /// quoted or expanded value is not read here: the command is then rewritten, and dtk honours the variable when
    /// it runs.
    /// </summary>
    /// <param name="word">A word of the command.</param>
    private static bool IsRawOutputAssignment(ReadOnlySpan<char> word)
    {
        const string assignment = RawOutputSwitch.EnvironmentVariable + "=";
        if (!word.StartsWith(assignment, StringComparison.Ordinal))
        {
            return false;
        }

        var value = word[assignment.Length..];
        return !value.ContainsAny(ShellValueCharacters) && RawOutputSwitch.IsOnFor(value.ToString());
    }

    private static int SkipSpaces(string command, int index)
    {
        while (index < command.Length && IsBlank(command[index]))
        {
            index++;
        }

        return index;
    }

    /// <summary>
    /// A shell blank, space or tab: the only characters that separate words on one line. A line break ends the
    /// command, and other whitespace (<c>\v</c>, no-break space, …) is part of a word.
    /// </summary>
    /// <param name="c">The character to test.</param>
    private static bool IsBlank(char c) => c is ' ' or '\t';

    private static bool IsWordBefore(string command, int index) =>
        index > 0
        && Rune.DecodeLastFromUtf16(command.AsSpan(0, index), out var rune, out _) == OperationStatus.Done
        && IsPythonWord(rune);

    private static bool IsWordAt(string command, int index) =>
        index < command.Length
        && Rune.DecodeFromUtf16(command.AsSpan(index), out var rune, out _) == OperationStatus.Done
        && IsPythonWord(rune);

    /// <summary>Python's <c>\w</c>: an underscore, a letter, or any numeric character, including superscripts.</summary>
    /// <param name="rune">The code point to test.</param>
    private static bool IsPythonWord(Rune rune) =>
        rune.Value == '_'
        || Rune.GetUnicodeCategory(rune) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
            or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber;

    /// <summary>What <see cref="ShellScanner"/> found at an index.</summary>
    private enum ShellToken
    {
        /// <summary>An unquoted, unescaped character of shell code, outside comments and here-document bodies.</summary>
        Code = 0,

        /// <summary>The <c>$(</c> or backtick that opens a command substitution, quoted or not.</summary>
        Substitution = 1,

        /// <summary>The <c>&lt;&lt;</c> of a here-document redirection.</summary>
        HereDocument = 2,

        /// <summary>
        /// A construct parsed for rewriting but never trusted for approval: a <c>#</c> comment, an ANSI-C
        /// <c>$'…'</c> or locale <c>$"…"</c> string, the <c>$</c> of a <c>${…}</c> expansion or of legacy
        /// <c>$[…]</c> arithmetic, or a <c>!</c>, which history expansion rewrites in shells that enable it.
        /// </summary>
        Unvetted = 3
    }

    /// <summary>A construct <see cref="ShellScanner"/> is inside of.</summary>
    private enum Frame : byte
    {
        /// <summary>No construct: the command itself.</summary>
        TopLevel = 0,

        /// <summary>A double-quoted string.</summary>
        DoubleQuote = 1,

        /// <summary>A <c>$(</c> substitution or a <c>(</c> subshell, closed by <c>)</c>.</summary>
        Parenthesis = 2,

        /// <summary>A backtick substitution.</summary>
        Backtick = 3
    }

    /// <summary>A here-document waiting for the end of its line, when its body starts.</summary>
    /// <param name="Delimiter">The delimiter word, quotes and escapes removed.</param>
    /// <param name="StripTabs">Whether <c>&lt;&lt;-</c> strips leading tabs from each body line.</param>
    private readonly record struct HereDocument(string Delimiter, bool StripTabs);

    /// <summary>
    /// Walks a shell command once, left to right, reporting each character of shell code and each construct
    /// that can run or feed a command, and skipping quoted text, comments and here-document bodies.
    /// </summary>
    /// <param name="command">The command to scan.</param>
    private ref struct ShellScanner(string command)
    {
        private Stack<Frame>? _frames;
        private List<HereDocument>? _pendingHereDocuments;
        private int _position;
        private bool _wordStart = true;
        private bool _unterminated;

        /// <summary>Whether every quote, substitution and here-document the scan opened was closed.</summary>
        internal readonly bool IsTerminated() => !_unterminated && Top == Frame.TopLevel;

        private readonly Frame Top => _frames is { Count: > 0 } frames ? frames.Peek() : Frame.TopLevel;

        /// <summary>Advances to the next reported index.</summary>
        /// <param name="index">The index of the character or construct found.</param>
        /// <param name="token">What was found there.</param>
        /// <returns><see langword="false"/> once the command is exhausted.</returns>
        internal bool MoveNext(out int index, out ShellToken token)
        {
            while (_position < command.Length)
            {
                index = _position;
                if (Top == Frame.DoubleQuote ? TryScanQuoted(out token) : TryScanCode(out token))
                {
                    return true;
                }
            }

            index = -1;
            token = ShellToken.Code;
            return false;
        }

        private bool TryScanQuoted(out ShellToken token)
        {
            switch (command[_position++])
            {
                case '\\':
                    _position++;
                    break;
                case '"':
                    _frames!.Pop();
                    _wordStart = false;
                    break;
                case '`':
                    return OpenSubstitution(Frame.Backtick, out token);
                case '$' when Peek() == '$':
                    _position++;
                    break;
                case '$' when Peek() is '{' or '[':
                case '!':
                    token = ShellToken.Unvetted;
                    return true;
                case '$' when Peek() == '(':
                    _position++;
                    return OpenSubstitution(Frame.Parenthesis, out token);
            }

            token = ShellToken.Code;
            return false;
        }

        private bool TryScanCode(out ShellToken token)
        {
            var c = command[_position++];
            token = ShellToken.Code;
            switch (c)
            {
                case '\\':
                    _position++;
                    _wordStart = false;
                    return false;
                case '\'':
                    SkipQuoted('\'', false);
                    return false;
                case '$' when Peek() == '$':
                    _position++;
                    _wordStart = false;
                    return true;
                case '$' when Peek() == '\'':
                    _position++;
                    SkipQuoted('\'', true);
                    token = ShellToken.Unvetted;
                    return true;
                case '$' when Peek() == '"':
                    _position++;
                    Push(Frame.DoubleQuote);
                    _wordStart = false;
                    token = ShellToken.Unvetted;
                    return true;
                case '$' when Peek() is '{' or '[':
                case '!':
                    _wordStart = false;
                    token = ShellToken.Unvetted;
                    return true;
                case '$' when Peek() == '(':
                    _position++;
                    return OpenSubstitution(Frame.Parenthesis, out token);
                case '"':
                    Push(Frame.DoubleQuote);
                    _wordStart = false;
                    return false;
                case '`' when Top == Frame.Backtick:
                    _frames!.Pop();
                    _wordStart = false;
                    return true;
                case '`':
                    return OpenSubstitution(Frame.Backtick, out token);
                case '#' when _wordStart:
                    var lineEnd = command.IndexOf('\n', _position);
                    _position = lineEnd < 0 ? command.Length : lineEnd;
                    token = ShellToken.Unvetted;
                    return true;
                case '(':
                    Push(Frame.Parenthesis);
                    _wordStart = true;
                    return true;
                case ')':
                    if (Top == Frame.Parenthesis)
                    {
                        _frames!.Pop();
                    }

                    _wordStart = false;
                    return true;
                case '<' when Peek() == '<' && Peek(1) == '<':
                    _position += 2;
                    _wordStart = false;
                    return true;
                case '<' when Peek() == '<':
                    _position++;
                    ReadHereDocumentDelimiter();
                    token = ShellToken.HereDocument;
                    return true;
                case '\n':
                    _wordStart = true;
                    SkipHereDocumentBodies();
                    return true;
                default:
                    _wordStart = c is ' ' or '\t' or ';' or '&' or '|';
                    return true;
            }
        }

        private bool OpenSubstitution(Frame frame, out ShellToken token)
        {
            Push(frame);
            _wordStart = true;
            token = ShellToken.Substitution;
            return true;
        }

        private void Push(Frame frame) => (_frames ??= new Stack<Frame>()).Push(frame);

        private readonly char Peek(int offset = 0) =>
            _position + offset < command.Length ? command[_position + offset] : '\0';

        /// <summary>Skips past the closing <paramref name="quote"/>, honoring backslash escapes only when <paramref name="escapes"/> is set.</summary>
        /// <param name="quote">The closing quote character.</param>
        /// <param name="escapes">Whether a backslash escapes the next character, as in <c>$'…'</c>.</param>
        private void SkipQuoted(char quote, bool escapes)
        {
            _wordStart = false;
            while (_position < command.Length)
            {
                var c = command[_position++];
                if (escapes && c == '\\')
                {
                    _position++;
                }
                else if (c == quote)
                {
                    return;
                }
            }

            _unterminated = true;
        }

        /// <summary>Reads the delimiter word after <c>&lt;&lt;</c> or <c>&lt;&lt;-</c> and queues its body.</summary>
        private void ReadHereDocumentDelimiter()
        {
            _wordStart = false;
            var stripTabs = Peek() == '-';
            if (stripTabs)
            {
                _position++;
            }

            while (Peek() is ' ' or '\t')
            {
                _position++;
            }

            var delimiter = new StringBuilder();
            while (_position < command.Length)
            {
                var c = command[_position];
                if (c is ' ' or '\t' or '\n' or ';' or '&' or '|' or '(' or ')' or '<' or '>')
                {
                    break;
                }

                _position++;
                if (c is '\'' or '"')
                {
                    AppendQuoted(delimiter, c);
                }
                else if (c == '\\' && _position < command.Length)
                {
                    delimiter.Append(command[_position++]);
                }
                else
                {
                    delimiter.Append(c);
                }
            }

            if (delimiter.Length == 0)
            {
                _unterminated = true;
                return;
            }

            (_pendingHereDocuments ??= []).Add(new HereDocument(delimiter.ToString(), stripTabs));
        }

        /// <summary>Appends a quoted part of a here-document delimiter, without its quotes.</summary>
        /// <param name="delimiter">The delimiter being read.</param>
        /// <param name="quote">The opening quote character.</param>
        private void AppendQuoted(StringBuilder delimiter, char quote)
        {
            while (_position < command.Length)
            {
                var c = command[_position++];
                if (c == quote)
                {
                    return;
                }

                if (quote == '"' && c == '\\' && Peek() is '\\' or '"' or '$' or '`')
                {
                    c = command[_position++];
                }

                delimiter.Append(c);
            }

            _unterminated = true;
        }

        /// <summary>At the start of a line, skips the bodies of the here-documents the previous line opened.</summary>
        private void SkipHereDocumentBodies()
        {
            if (_pendingHereDocuments is not { Count: > 0 } pending)
            {
                return;
            }

            foreach (var hereDocument in pending)
            {
                SkipHereDocumentBody(hereDocument);
            }

            pending.Clear();
        }

        private void SkipHereDocumentBody(HereDocument hereDocument)
        {
            while (_position < command.Length)
            {
                var lineEnd = command.IndexOf('\n', _position);
                var end = lineEnd < 0 ? command.Length : lineEnd;
                var line = command.AsSpan(_position, end - _position);
                _position = lineEnd < 0 ? end : end + 1;
                if ((hereDocument.StripTabs ? line.TrimStart('\t') : line).SequenceEqual(hereDocument.Delimiter))
                {
                    return;
                }
            }

            _unterminated = true;
        }
    }
}
