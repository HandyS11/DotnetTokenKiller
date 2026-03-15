using Microsoft.ML.Tokenizers;

namespace DotnetTokenKiller.Application.Helpers;

public static class TokenEstimator
{
    private static readonly Lazy<TiktokenTokenizer> Tokenizer =
        new(() => TiktokenTokenizer.CreateForEncoding("cl100k_base"));

    public static int Estimate(string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : Tokenizer.Value.CountTokens(text);
    }
}
