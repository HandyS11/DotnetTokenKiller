using System.Globalization;
using System.Text;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Benchmarks.Corpus;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Text;
using FluentAssertions;
using Microsoft.ML.Tokenizers;
using Xunit;
using Xunit.Abstractions;

namespace DotnetTokenKiller.Application.Tests.Helpers;

public class ChunkedTokenCounterTests(ITestOutputHelper output)
{
    private const string Esc = "\e";
    private const string Bel = "\a";
    private const string St = Esc + "\\";

    /// <summary>Small enough that every repeated input is cut many times; production uses 64 K chars.</summary>
    private const int TinyChunk = 64;

    /// <summary>The generated tiers reach 1 MB; a tiny chunk would make them slow without covering more.</summary>
    private const int GeneratedChunk = 4096;

    private static readonly TokenizerModel[] Models = [TokenizerModel.Cl100kBase, TokenizerModel.O200kBase];

    private static readonly Lazy<TiktokenTokenizer> Cl100k = new(() => TiktokenTokenizer.CreateForEncoding("cl100k_base"));
    private static readonly Lazy<TiktokenTokenizer> O200k = new(() => TiktokenTokenizer.CreateForEncoding("o200k_base"));

    private static readonly string[] GeneratedFilterKeys =
        [FilterKeys.Build, FilterKeys.Test, FilterKeys.Restore, FilterKeys.Clean, FilterKeys.Format, FilterKeys.ListPackage];

    private static readonly string[] Trailings =
    [
        "",
        "error: stderr said something\n   at Program.Main(String[] args)\n",
        "  starts with spaces\n",
        "/starts/with/a/slash",
    ];

    /// <summary>Odd sizes, so appends end mid-line, mid-escape sequence and mid-surrogate pair.</summary>
    private static readonly int[] PieceSizes = [1, 7, 37, 3, 101, 13];

    /// <summary>How many seeded random strings the proofs also check.</summary>
    private const int FuzzCount = 5000;

    /// <summary>What the fuzz strings are built from: everything the cut rules single out, and some of what they do not.</summary>
    private static readonly string[] FuzzPieces =
    [
        "\n", "\n", "\n", "\r\n", "\r", " ", "  ", "\t", "\f", "\u0085", "\u2028", "\u00a0",
        "/", "//", ";", ".", "]", "[", "'", "'s", "\\", Bel,
        "a", "Word", "m", "0", "1", "123", "é", "日本", "🚀", "\u0301",
        Esc, Esc + "[", Esc + "[0m", Esc + "[31m", Esc + "]0;", St, "<|endoftext|>",
    ];

    private static readonly string[] AdversarialStrings =
    [
        // Runs of spaces, tabs and newlines.
        "a \t \n\n \t\n\t  \n b",
        "\t\t\n \n\t",
        "   \n   \n   x",
        "x\n\n\n\n",
        "\n\n\nx",
        new('\n', 200),
        "a\n\n  x",
        "foo \n  bar\n\tbaz",
        "\n \n  x",
        "a\n\v x\n\f/y\n\u001c z\n",

        // Punctuation before newlines, and slashes after them.
        "x;\n\ny",
        "]\n/path",
        "a/\n/b",
        ";\n/\n/a",
        "path\n/usr/bin\n/etc\n",
        "word\n/a",
        "1\n/a",
        " \n/a",
        "\n/a",
        "\n\n/a",
        "é\n/a",
        "x;\r\n/a",
        "a/\r\n\r\n//b",
        "[x]\n/\n/",
        "a\n//\n/b",
        "a\u0085\n/b",
        "abc1m\n/x",
        "error CS0001: boom [/repo/x.csproj]\n/repo/y.cs(1,1): warning\n  /indented\n",

        // CRLF and lone carriage returns.
        "line\r\nnext\r\n\r\nlast",
        "\r\n  x",
        "a\r\n\r\n  b\r\n/c\r\n",
        "a\r\r\n b",
        "a\n\r b",
        "a\n \r\nb",

        // Escape sequences right after newlines, and hiding the character before them.
        "a\n\n" + Esc + "[0m\n  x",
        "line\n" + Esc + "[31merror\n",
        "a\n  " + Esc + "[0m  x\n",
        "x;" + Esc + "[0m\n/a",
        "x;\n" + Esc + "[0m\n/a",
        "x;" + Esc + Esc + "]t" + Bel + "[0m\n/a",
        "x;" + Esc + "]0;t" + Bel + "\n/a",
        "x;" + Esc + "]0;t" + St + "\n/a",
        "done." + Esc + "[1m\n  /a\n",
        Esc + "[31mred" + Esc + "[0m\nplain\n",

        // CSI and OSC sequences spanning lines: terminated by BEL, by ST, and never.
        Esc + "]0;title\nmore" + Bel + "after\nx\n",
        Esc + "]8;;http://x\nline\n" + St + "tail\nnext\n",
        Esc + "]0;never terminated\nline\nline\n",
        Esc + "[31m\nrest\n",
        Esc + "[12\nnot a csi\nline\n",
        "a\n" + Esc + "\nb\n",
        "a\n" + Esc + "]0;x\n" + Esc + "[0m\nb\n",
        Esc + "\n" + Esc + "]t" + Bel + "[0m\nz\n",

        // Special tokens between lines.
        "<|endoftext|>\nx\n<|endoftext|>",
        "a\n<|endoftext|>\n  <|endoftext|>b\n/<|endoftext|>",

        // Non-ASCII letters, CJK, emoji and combining marks at line starts.
        "é ñ 日本語\n中文\n🚀 emoji\n",
        "x\n\u0301combining\n e\u0301\n",
        "a;\n🚀\n/🚀\n",
        "x\nÉcole\n/ü\n",

        // Contractions and digits at line starts.
        "don't\n're\n",
        "don't\nwe're\nI'll\n",
        "x\n's\n'LL\n",
        "123456\n7890\n",
        "12\n345\n/6\n",

        // No newline, nothing, a trailing newline.
        "no newline at all",
        "",
        "trailing newline\n",

        // Unicode line separators inside lines.
        "a\u2028b\nc\u0085d\n",
        "x\n\u2028y\n\u0085\nz",
        "x\n\u00a0\u2028 y\n",

        // Longer shapes.
        string.Concat(Enumerable.Repeat("word ", 2000)),
        string.Concat(Enumerable.Repeat("error CS0001: boom\n", 100)),
    ];

    public static TheoryData<string> FixtureNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in FixtureCorpus.Names)
        {
            data.Add(name);
        }

        return data;
    }

    /// <summary>By index: some strings hold control characters that test discovery should not have to serialize.</summary>
    public static TheoryData<int> AdversarialIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < AdversarialStrings.Length; i++)
        {
            data.Add(i);
        }

        return data;
    }

    public static TheoryData<string, CorpusTier> GeneratedLogs()
    {
        var data = new TheoryData<string, CorpusTier>();
        foreach (var key in GeneratedFilterKeys)
        {
            foreach (var tier in Enum.GetValues<CorpusTier>())
            {
                data.Add(key, tier);
            }
        }

        return data;
    }

    [Fact]
    public void EverySafeCut_SplitsTheCountExactly()
    {
        var failures = new List<string>();
        var cuts = 0;

        foreach (var (name, text) in ProofInputs())
        {
            foreach (var model in Models)
            {
                // The token ids as well as the count: two different splits can have the same length by chance.
                var tokenizer = (model == TokenizerModel.Cl100kBase ? Cl100k : O200k).Value;
                var whole = Count(text, model);
                var wholeIds = tokenizer.EncodeToIds(AnsiStrip.Strip(text));
                var lastSafe = -1;
                var cutsInText = 0;
                for (var cut = 1; cut < text.Length; cut++)
                {
                    if (!ChunkedTokenCounter.IsSafeCut(text, cut, model))
                    {
                        continue;
                    }

                    cutsInText++;
                    lastSafe = cut;
                    var sum = Count(text[..cut], model) + Count(text[cut..], model);
                    if (sum != whole)
                    {
                        failures.Add($"{name}, {model}, cut {cut}: {sum} != {whole}, around {Show(text, cut)}");
                    }

                    int[] ids =
                    [
                        .. tokenizer.EncodeToIds(AnsiStrip.Strip(text[..cut])),
                        .. tokenizer.EncodeToIds(AnsiStrip.Strip(text[cut..])),
                    ];
                    if (!ids.SequenceEqual(wholeIds))
                    {
                        failures.Add($"{name}, {model}, cut {cut}: token ids differ, around {Show(text, cut)}");
                    }
                }

                if (name.StartsWith("fixture", StringComparison.Ordinal) && cutsInText == 0)
                {
                    failures.Add($"{name}, {model}: no safe cut at all, so the fixture proves nothing");
                }

                var found = ChunkedTokenCounter.FindLastSafeCut(text, model);
                if (found != lastSafe)
                {
                    failures.Add($"{name}, {model}: FindLastSafeCut returned {found}, the last safe cut is {lastSafe}");
                }

                cuts += cutsInText;
            }
        }

        output.WriteLine($"Checked {cuts} safe cuts.");
        failures.Should().BeEmpty();
    }

    [Fact]
    public void FindLastSafeCut_MatchesIsSafeCut_ForEveryPrefix()
    {
        var failures = new List<string>();
        foreach (var (name, text) in ShortInputs())
        {
            foreach (var model in Models)
            {
                for (var length = 0; length <= text.Length; length++)
                {
                    var prefix = text[..length];
                    var expected = Enumerable.Range(1, Math.Max(0, length - 1))
                        .LastOrDefault(cut => ChunkedTokenCounter.IsSafeCut(prefix, cut, model), -1);
                    var found = ChunkedTokenCounter.FindLastSafeCut(prefix, model);
                    if (found != expected)
                    {
                        failures.Add($"{name}, {model}, length {length}: {found} != {expected}");
                    }
                }
            }
        }

        failures.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public async Task Fixture_ChunkedCountEqualsWholeCount(string name)
    {
        var estimates = await AssertChunkedExactAsync(Repeat(FixtureCorpus.Load(name), 8 * TinyChunk), TinyChunk);

        estimates.Should().BeGreaterThanOrEqualTo(2 * Models.Length * Trailings.Length, "every run should cut at least once");
    }

    [Theory]
    [MemberData(nameof(AdversarialIndexes))]
    public async Task Adversarial_ChunkedCountEqualsWholeCount(int index)
    {
        await AssertChunkedExactAsync(Repeat(AdversarialStrings[index], 8 * TinyChunk), TinyChunk);
    }

    [Fact]
    public async Task Fuzz_ChunkedCountEqualsWholeCount()
    {
        var estimates = await AssertChunkedExactAsync(
            string.Join('\n', Enumerable.Range(0, FuzzCount).Select(Fuzz)), TinyChunk);

        estimates.Should().BeGreaterThan(Models.Length * Trailings.Length * 50);
    }

    [Theory]
    [MemberData(nameof(GeneratedLogs))]
    public async Task GeneratedLog_ChunkedCountEqualsWholeCount(string filterKey, CorpusTier tier)
    {
        await AssertChunkedExactAsync(LogCorpusGenerator.Generate(filterKey, tier), GeneratedChunk);
    }

    [Theory]
    [InlineData(TokenizerModel.Cl100kBase)]
    [InlineData(TokenizerModel.O200kBase)]
    public async Task LargeBuildLog_IsCountedInManyChunks_AtTheDefaultChunkSize(TokenizerModel model)
    {
        var text = LogCorpusGenerator.Generate(FilterKeys.Build, CorpusTier.Large);
        var calls = 0;
        var counter = new ChunkedTokenCounter(model, ChunkedTokenCounter.DefaultMinChunkChars, (chunk, m) =>
        {
            Interlocked.Increment(ref calls);
            return TokenEstimator.Estimate(chunk, m);
        });

        // Line by line, as a writer that forwards each output line would feed it.
        foreach (var line in text.Split('\n'))
        {
            counter.Append(line);
            counter.Append("\n");
        }

        counter.Finish(string.Empty);

        (await counter.TotalAsync()).Should().Be(Count(text + "\n", model));
        output.WriteLine($"{model}: {calls} estimates for {text.Length} chars.");
        calls.Should().BeGreaterThanOrEqualTo(10);
    }

    [Theory]
    [InlineData("ab\ncd\nef", TokenizerModel.Cl100kBase, 6)]
    [InlineData("ab\ncd\n ef", TokenizerModel.Cl100kBase, 6)]
    [InlineData("ab\n\n  cd", TokenizerModel.Cl100kBase, 4)]
    [InlineData("ab\n \ncd", TokenizerModel.Cl100kBase, 5)]
    [InlineData("ab\ncd\n", TokenizerModel.Cl100kBase, 3)]
    [InlineData("ab\ncd\n  ", TokenizerModel.Cl100kBase, 3)]
    [InlineData("ab\ncd\r\n ef", TokenizerModel.Cl100kBase, 7)]
    [InlineData("ab\ncd\n\r ef", TokenizerModel.Cl100kBase, 3)]
    [InlineData("abcdef", TokenizerModel.Cl100kBase, -1)]
    [InlineData("ab\ncd;\n/ef", TokenizerModel.Cl100kBase, 7)]
    [InlineData("ab\ncd;\n/ef", TokenizerModel.O200kBase, 3)]
    [InlineData("ab\ncd\n/ef", TokenizerModel.O200kBase, 6)]
    [InlineData("ab\n12\n/ef", TokenizerModel.O200kBase, 6)]
    [InlineData("ab\ncd \n/ef", TokenizerModel.O200kBase, 7)]
    [InlineData("\n\n/ef", TokenizerModel.O200kBase, -1)]
    [InlineData(Esc + "]title\nrest\nmore", TokenizerModel.Cl100kBase, -1)]
    [InlineData(Esc + "[0m\nrest\nmore", TokenizerModel.Cl100kBase, 10)]
    [InlineData("ab\n" + Esc + "[0m\ncd", TokenizerModel.Cl100kBase, 8)]
    [InlineData("x;" + Esc + "[0m\n/a", TokenizerModel.Cl100kBase, 7)]
    [InlineData("x;" + Esc + "[0m\n/a", TokenizerModel.O200kBase, -1)]
    public void FindLastSafeCut_FindsTheLastCutNoPreTokenOrEscapeSequenceSpans(string text, TokenizerModel model,
        int expected)
    {
        ChunkedTokenCounter.FindLastSafeCut(text, model).Should().Be(expected);
    }

    [Fact]
    public async Task Append_AfterFinish_Throws_AndTotalBeforeFinish_Throws()
    {
        var counter = new ChunkedTokenCounter(TokenizerModel.Cl100kBase);

        var total = () => counter.TotalAsync();
        await total.Should().ThrowAsync<InvalidOperationException>();

        counter.Finish(string.Empty);
        var append = () => counter.Append("x");
        append.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Finish_CalledTwice_KeepsTheFirstTrailingText()
    {
        var counter = new ChunkedTokenCounter(TokenizerModel.Cl100kBase);
        counter.Append("hello\n");

        counter.Finish("first");
        counter.Finish(" and a much longer second trailing text that would change the count");

        (await counter.TotalAsync()).Should().Be(TokenEstimator.Estimate("hello\nfirst"));
    }

    [Fact]
    public async Task TotalAsync_PropagatesAFailedEstimate()
    {
        var counter = new ChunkedTokenCounter(TokenizerModel.Cl100kBase, TinyChunk,
            static (_, _) => throw new InvalidOperationException("no vocabulary"));
        counter.Append(Repeat("some text\nmore\n", 4 * TinyChunk));
        counter.Finish(string.Empty);

        var total = () => counter.TotalAsync();

        await total.Should().ThrowAsync<InvalidOperationException>().WithMessage("no vocabulary");
    }

    private static IEnumerable<(string Name, string Text)> ProofInputs()
    {
        foreach (var name in FixtureCorpus.Names)
        {
            yield return ($"fixture {name}", FixtureCorpus.Load(name));
        }

        foreach (var key in GeneratedFilterKeys)
        {
            yield return ($"generated {key} small", LogCorpusGenerator.Generate(key, CorpusTier.Small));
        }

        for (var i = 0; i < AdversarialStrings.Length; i++)
        {
            yield return ($"adversarial[{i}]", AdversarialStrings[i]);
        }

        // Every pair of neighbours meets across a line break and without one.
        yield return ("adversarial joined by newlines", string.Join('\n', AdversarialStrings.Where(s => s.Length < 1000)));
        yield return ("adversarial concatenated", string.Concat(AdversarialStrings.Where(s => s.Length < 1000)));

        for (var i = 0; i < FuzzCount; i++)
        {
            yield return ($"fuzz[{i}]", Fuzz(i));
        }
    }

    private static IEnumerable<(string Name, string Text)> ShortInputs()
    {
        for (var i = 0; i < AdversarialStrings.Length; i++)
        {
            if (AdversarialStrings[i].Length < 1000)
            {
                yield return ($"adversarial[{i}]", AdversarialStrings[i]);
            }
        }

        for (var i = 0; i < FuzzCount; i++)
        {
            yield return ($"fuzz[{i}]", Fuzz(i));
        }
    }

    /// <summary>A seeded random string of <see cref="FuzzPieces"/>, identical on every machine and every run.</summary>
    /// <param name="index">Which string.</param>
    private static string Fuzz(int index)
    {
        var state = ((uint)index * 2654435761u) + 12345u;
        var pieces = 1 + (int)(NextRandom(ref state) % 40);
        var builder = new StringBuilder();
        for (var i = 0; i < pieces; i++)
        {
            builder.Append(FuzzPieces[NextRandom(ref state) % (uint)FuzzPieces.Length]);
        }

        return builder.ToString();
    }

    /// <summary>A linear congruential step; the high bits are the better distributed ones.</summary>
    /// <param name="state">The generator state, advanced in place.</param>
    private static uint NextRandom(ref uint state)
    {
        state = (state * 1664525u) + 1013904223u;
        return state >> 8;
    }

    /// <summary>
    /// Feeds <paramref name="text"/> in odd-sized pieces, for both models and every trailing text,
    /// and asserts the chunked total equals the whole-text count. Returns how many estimates ran.
    /// </summary>
    /// <param name="text">The stdout to feed.</param>
    /// <param name="minChunkChars">The counter's chunk size.</param>
    private static async Task<int> AssertChunkedExactAsync(string text, int minChunkChars)
    {
        var calls = 0;
        foreach (var model in Models)
        {
            foreach (var trailing in Trailings)
            {
                var counter = new ChunkedTokenCounter(model, minChunkChars, (chunk, m) =>
                {
                    Interlocked.Increment(ref calls);
                    return TokenEstimator.Estimate(chunk, m);
                });

                var start = 0;
                var piece = 0;
                while (start < text.Length)
                {
                    var size = Math.Min(PieceSizes[piece++ % PieceSizes.Length], text.Length - start);
                    counter.Append(text.AsSpan(start, size));
                    start += size;
                }

                counter.Finish(trailing);

                (await counter.TotalAsync()).Should().Be(Count(text + trailing, model),
                    "chunking must not change the count for {0} with trailing {1}", model, Show(trailing, 0));
            }
        }

        return calls;
    }

    private static int Count(string text, TokenizerModel model) => TokenEstimator.Estimate(AnsiStrip.Strip(text), model);

    private static string Repeat(string text, int atLeast)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var builder = new StringBuilder(atLeast + text.Length);
        do
        {
            builder.Append(text);
        } while (builder.Length < atLeast);

        return builder.ToString();
    }

    private static string Show(string text, int at)
    {
        var start = Math.Max(0, at - 20);
        var end = Math.Min(text.Length, at + 20);
        var builder = new StringBuilder("\"");
        for (var i = start; i < end; i++)
        {
            if (i == at && at > 0)
            {
                builder.Append('|');
            }

            var c = text[i];
            builder.Append(c is >= ' ' and <= '~'
                ? c.ToString()
                : "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture));
        }

        return builder.Append('"').ToString();
    }
}
