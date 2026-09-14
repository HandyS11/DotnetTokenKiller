using System.Text;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Text;

namespace DotnetTokenKiller.Application.Helpers;

/// <summary>
/// Counts the tokens of text that arrives while a child runs, in chunks on the thread pool, so that
/// only the last chunk is counted after the child has exited.
/// </summary>
/// <remarks>
/// <para>
/// The sum over chunks equals <see cref="TokenEstimator.Estimate"/> of the stripped whole text
/// because a chunk ends only at a cut <see cref="IsSafeCut"/> accepts, which neither an escape
/// sequence nor a tiktoken pre-token spans, so <see cref="AnsiStrip.Strip"/> and the tokenizer see
/// the same pieces either way. <c>ChunkedTokenCounterTests</c> checks every accepted cut in the
/// fixture corpus, adversarial strings and seeded random strings, for both encodings.
/// </para>
/// <para>
/// Fed with stdout only; <see cref="Finish"/> takes the stderr text, so the total is the count of
/// stdout followed by stderr, as <see cref="TokenEstimator.Estimate"/> over the concatenation was.
/// </para>
/// </remarks>
public sealed class ChunkedTokenCounter
{
    /// <summary>
    /// How far the buffered text must grow before the counter looks for a cut again: about 3 ms of
    /// counting per chunk.
    /// </summary>
    public const int DefaultMinChunkChars = 64 * 1024;

    private readonly TokenizerModel _model;
    private readonly int _minChunkChars;
    private readonly Func<string, TokenizerModel, int> _estimate;
    private readonly StringBuilder _pending = new();
    private readonly List<Task<int>> _chunks = [];
    private readonly Lock _gate = new();
    private int _pendingAtLastSearch;
    private Task<int>? _final;

    /// <summary>Creates a counter for <paramref name="model"/>.</summary>
    /// <param name="model">The tokenizer to count with.</param>
    /// <param name="minChunkChars">How far the buffered text must grow before the counter looks for a cut again.</param>
    public ChunkedTokenCounter(TokenizerModel model, int minChunkChars = DefaultMinChunkChars)
        : this(model, minChunkChars, TokenEstimator.Estimate)
    {
    }

    /// <summary>For tests: a counter whose estimate can be observed or made to fail.</summary>
    /// <param name="model">The tokenizer to count with.</param>
    /// <param name="minChunkChars">How far the buffered text must grow before the counter looks for a cut again.</param>
    /// <param name="estimate">Counts the tokens of one stripped chunk.</param>
    internal ChunkedTokenCounter(TokenizerModel model, int minChunkChars, Func<string, TokenizerModel, int> estimate)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minChunkChars, 1);
        ArgumentNullException.ThrowIfNull(estimate);

        _model = model;
        _minChunkChars = minChunkChars;
        _estimate = estimate;
    }

    /// <summary>
    /// Adds text. Each time the buffer has grown by the chunk size, counts everything up to its last
    /// safe cut on the thread pool.
    /// </summary>
    /// <param name="text">The next piece of stdout, in order.</param>
    /// <exception cref="InvalidOperationException"><see cref="Finish"/> was already called.</exception>
    public void Append(ReadOnlySpan<char> text)
    {
        lock (_gate)
        {
            if (_final is not null)
            {
                throw new InvalidOperationException("The counter has been finished.");
            }

            _pending.Append(text);
            if (_pending.Length - _pendingAtLastSearch < _minChunkChars)
            {
                return;
            }

            var buffered = _pending.ToString();
            var cut = FindLastSafeCut(buffered, _model);
            if (cut > 0)
            {
                var chunk = buffered[..cut];
                _pending.Remove(0, cut);
                _chunks.Add(Task.Run(() => _estimate(AnsiStrip.Strip(chunk), _model)));
            }

            _pendingAtLastSearch = _pending.Length;
        }
    }

    /// <summary>Counts what remains plus <paramref name="trailing"/> as the final chunk. Later calls do nothing.</summary>
    /// <param name="trailing">The stderr text, appended after everything <see cref="Append"/> saw.</param>
    public void Finish(string trailing)
    {
        ArgumentNullException.ThrowIfNull(trailing);

        lock (_gate)
        {
            if (_final is not null)
            {
                return;
            }

            var last = _pending.Append(trailing).ToString();
            _pending.Clear();
            _final = Task.Run(() => _estimate(AnsiStrip.Strip(last), _model));
        }
    }

    /// <summary>The total over every chunk. Throws what any chunk's estimate threw.</summary>
    /// <exception cref="InvalidOperationException"><see cref="Finish"/> was not called.</exception>
    public async Task<int> TotalAsync()
    {
        Task<int>[] all;
        lock (_gate)
        {
            var final = _final ?? throw new InvalidOperationException("Call Finish before TotalAsync.");
            all = [.. _chunks, final];
        }

        var counts = await Task.WhenAll(all).ConfigureAwait(false);
        return counts.Sum();
    }

    /// <summary>The largest cut in <paramref name="text"/> that <see cref="IsSafeCut"/> accepts, or -1.</summary>
    /// <param name="text">The buffered text.</param>
    /// <param name="model">The tokenizer the chunks will be counted with.</param>
    internal static int FindLastSafeCut(string text, TokenizerModel model)
    {
        var cut = text.Length - 1;
        while (cut > 0)
        {
            if (!SplitsNoPreToken(text, cut, model))
            {
                cut--;
            }
            else if (AnsiStrip.EndsInsideEscapeSequence(text.AsSpan(0, cut)))
            {
                // Every shorter prefix that still holds the same last ESC ends inside it too: skip to that ESC.
                cut = text.LastIndexOf('\e', cut - 1);
            }
            else
            {
                return cut;
            }
        }

        return -1;
    }

    /// <summary>
    /// Whether counting <c>text[..cut]</c> and <c>text[cut..]</c> apart, each stripped, is known to
    /// give the count of the whole stripped text.
    /// </summary>
    /// <remarks>
    /// <para>A cut is safe when all of these hold:</para>
    /// <list type="number">
    /// <item><description><c>text[cut - 1]</c> is a line feed.</description></item>
    /// <item><description>
    /// The first character at or after <paramref name="cut"/> that is not whitespace exists, and no
    /// carriage return or line feed comes before it. The line-break patterns (<c>\s*[\r\n]</c> in
    /// <c>cl100k_base</c>, <c>\s*[\r\n]+</c> in <c>o200k_base</c>) end at the last line break of a
    /// whitespace run, so the whitespace after the cut is a pre-token of its own either way.
    /// </description></item>
    /// <item><description>
    /// That character is not ESC, so stripping cannot remove it and join whitespace across the cut.
    /// </description></item>
    /// <item><description>
    /// For <c>o200k_base</c>, if it is <c>/</c> at <paramref name="cut"/>, the last character before
    /// the line breaks is whitespace, a letter or a number, and still is once stripped: its
    /// punctuation pattern <c> ?[^\s\p{L}\p{N}]+[\r\n/]*</c> would otherwise take the line breaks
    /// and the slash. An ASCII letter right after <c>[</c>, <c>;</c>, an ASCII digit, BEL or
    /// <c>\</c> does not count, since it may end a CSI sequence that stripping removes.
    /// </description></item>
    /// <item><description>
    /// <c>text[..cut]</c> does not end inside an escape sequence
    /// (<see cref="AnsiStrip.EndsInsideEscapeSequence"/>), so each side strips as it does in the whole.
    /// </description></item>
    /// </list>
    /// </remarks>
    /// <param name="text">The text to cut.</param>
    /// <param name="cut">The index of the first character of the second part.</param>
    /// <param name="model">The tokenizer the parts will be counted with.</param>
    internal static bool IsSafeCut(string text, int cut, TokenizerModel model)
    {
        return SplitsNoPreToken(text, cut, model) && !AnsiStrip.EndsInsideEscapeSequence(text.AsSpan(0, cut));
    }

    /// <summary>Rules 1 to 4 of <see cref="IsSafeCut"/>.</summary>
    /// <param name="text">The text to cut.</param>
    /// <param name="cut">The index of the first character of the second part.</param>
    /// <param name="model">The tokenizer the parts will be counted with.</param>
    private static bool SplitsNoPreToken(string text, int cut, TokenizerModel model)
    {
        if (cut <= 0 || cut >= text.Length || text[cut - 1] != '\n')
        {
            return false;
        }

        var next = cut;
        while (next < text.Length && char.IsWhiteSpace(text[next]))
        {
            if (text[next] is '\r' or '\n')
            {
                return false;
            }

            next++;
        }

        if (next == text.Length || text[next] == '\e')
        {
            return false;
        }

        return model == TokenizerModel.Cl100kBase || next > cut || text[cut] != '/' || EndsLineWithWordOrSpace(text, cut);
    }

    /// <summary>
    /// Rule 4: whether the last character before the line breaks that end at <paramref name="cut"/>
    /// is whitespace, a letter or a number once stripped, which keeps <c>o200k_base</c>'s punctuation
    /// pattern from reaching the line breaks.
    /// </summary>
    /// <param name="text">The text to cut.</param>
    /// <param name="cut">The index just after the line breaks.</param>
    private static bool EndsLineWithWordOrSpace(string text, int cut)
    {
        var last = cut - 1;
        while (last >= 0 && text[last] is '\r' or '\n')
        {
            last--;
        }

        if (last < 0)
        {
            return false;
        }

        var character = text[last];
        if (char.IsWhiteSpace(character) || char.IsNumber(character))
        {
            return true;
        }

        // An ASCII letter can end a CSI sequence that stripping removes, exposing punctuation before
        // the line breaks. It cannot when what comes before it is neither a CSI parameter nor '[',
        // nor the BEL or '\' that ends an OSC sequence stripping removes first.
        return char.IsLetter(character)
               && (!char.IsAsciiLetter(character) || last == 0 || !MayPrecedeCsiFinalByte(text[last - 1]));
    }

    private static bool MayPrecedeCsiFinalByte(char character)
    {
        return character is '[' or ';' or '\a' or '\\' || char.IsAsciiDigit(character);
    }
}
