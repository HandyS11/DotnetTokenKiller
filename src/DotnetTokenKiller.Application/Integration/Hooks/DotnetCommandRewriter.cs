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
/// <para>
/// A match is <c>dotnet</c>, blanks (spaces or tabs), and a subcommand (blanks between a multi-token
/// subcommand's tokens, the longest subcommand tried first, a word boundary after it). It is left alone when
/// the character before it cannot end a previous command (a path such as <c>/usr/lib/dotnet/dotnet build</c>),
/// when <c>dtk</c> already runs it, or when it is not shell code at all. <see cref="IsPythonWord"/> keeps the
/// Unicode-aware <c>\w</c> of the Python hooks dtk generated before <c>dtk hook</c>, which this class started as
/// a port of.
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

    /// <summary>Characters that may precede <c>dotnet</c> where a command starts.</summary>
    private static readonly SearchValues<char> Boundaries = SearchValues.Create(" \t;&|({`\n");

    /// <summary>Unquoted characters that chain, pipe, subshell or redirect, none of which a simple command has.</summary>
    private static readonly SearchValues<char> NotSimple = SearchValues.Create(";&|`\n()<>");

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
                && IsCommandPosition(command, start))
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
    /// <see cref="IsSimpleCommand">simple</see> and its subcommand is one of <see cref="AutoApprovableSubcommands"/>.
    /// <c>dotnet publish</c> and <c>dotnet pack</c> are still rewritten, but they write artifacts (and a publish
    /// profile can deploy), so they are left to the user's own approval.
    /// </summary>
    /// <param name="command">The original, unrewritten command.</param>
    internal static bool IsAutoApprovable(string command)
    {
        if (!IsSimpleCommand(command))
        {
            return false;
        }

        var position = command.AsSpan().IndexOf(Dotnet, StringComparison.Ordinal) + Dotnet.Length;
        var subcommandStart = SkipSpaces(command, position);
        foreach (var tokens in AutoApprovableTokens)
        {
            if (TryMatchTokens(command, subcommandStart, tokens, out var end) && !IsWordAt(command, end))
            {
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

    /// <summary>Whether the <c>dotnet</c> at <paramref name="start"/> follows a command boundary and is not run by <c>dtk</c>.</summary>
    /// <param name="command">The full command being scanned.</param>
    /// <param name="start">The index of the <c>dotnet</c> token being considered.</param>
    private static bool IsCommandPosition(string command, int start)
    {
        if (start > 0 && !Boundaries.Contains(command[start - 1]))
        {
            return false;
        }

        var preceding = command.AsSpan(0, start);
        var length = preceding.Length;
        while (length > 0 && IsBlank(preceding[length - 1]))
        {
            length--;
        }

        preceding = preceding[..length];
        var word = preceding[(preceding.LastIndexOfAny(Boundaries) + 1)..];
        var program = word[(word.LastIndexOfAny('/', '\\') + 1)..];
        return program is not ("dtk" or "dtk.exe");
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
