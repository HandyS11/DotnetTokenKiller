using System.Collections.Concurrent;
using DotnetTokenKiller.Domain.Configuration;
using Microsoft.ML.Tokenizers;

namespace DotnetTokenKiller.Application.Helpers;

/// <summary>Estimates token counts using a configurable tiktoken tokenizer.</summary>
public static class TokenEstimator
{
    private static readonly ConcurrentDictionary<TokenizerModel, Lazy<TiktokenTokenizer>> Tokenizers = new();

    /// <summary>Returns the estimated token count for the given text using the specified tokenizer model.</summary>
    /// <param name="text">The text to estimate tokens for.</param>
    /// <param name="model">The tokenizer model to use.</param>
    public static int Estimate(string text, TokenizerModel model = TokenizerModel.Cl100kBase)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return GetTokenizer(model).CountTokens(text);
    }

    /// <summary>Loads the tokenizer for <paramref name="model"/> if this process has not loaded it yet.</summary>
    /// <param name="model">The tokenizer model to load.</param>
    /// <remarks>
    /// The vocabulary load costs about 110 ms for <c>cl100k_base</c>, once per process. Calling this
    /// on a background thread while a child process runs moves that cost out of the serial path. An
    /// <see cref="Estimate"/> call made while the load is still running waits for that same load
    /// rather than starting another, so counts are identical either way.
    /// </remarks>
    public static void WarmUp(TokenizerModel model = TokenizerModel.Cl100kBase)
    {
        _ = GetTokenizer(model);
    }

    /// <summary>Returns the process-wide tokenizer for <paramref name="model"/>, loading it once.</summary>
    /// <param name="model">The tokenizer model.</param>
    /// <remarks>
    /// <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey, Func{TKey,TValue})"/> may build
    /// more than one <see cref="Lazy{T}"/> under a race, but every caller receives the one stored, and
    /// a <see cref="Lazy{T}"/> in its default thread-safe mode runs its factory once.
    /// </remarks>
    private static TiktokenTokenizer GetTokenizer(TokenizerModel model)
    {
        return Tokenizers.GetOrAdd(model,
                static m => new Lazy<TiktokenTokenizer>(() => TiktokenTokenizer.CreateForEncoding(ToEncodingName(m))))
            .Value;
    }

    private static string ToEncodingName(TokenizerModel model)
    {
        return model switch
        {
            TokenizerModel.Cl100kBase => "cl100k_base",
            TokenizerModel.O200kBase => "o200k_base",
            _ => "cl100k_base"
        };
    }
}
