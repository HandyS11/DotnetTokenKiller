using DotnetTokenKiller.Domain.Configuration;
using Microsoft.ML.Tokenizers;
using System.Collections.Concurrent;

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

        var tokenizer = Tokenizers.GetOrAdd(model,
            static m => new Lazy<TiktokenTokenizer>(() => TiktokenTokenizer.CreateForEncoding(ToEncodingName(m))));

        return tokenizer.Value.CountTokens(text);
    }

    private static string ToEncodingName(TokenizerModel model)
    {
        return model switch
        {
            TokenizerModel.Cl100kBase => "cl100k_base",
            TokenizerModel.O200kBase => "o200k_base",
            TokenizerModel.P50kBase => "p50k_base",
            TokenizerModel.R50kBase => "r50k_base",
            TokenizerModel.P50kEdit => "p50k_edit",
            _ => "cl100k_base"
        };
    }
}
