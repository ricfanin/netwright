using Microsoft.ML.Tokenizers;

namespace Netwright.Benchmarks;

/// <summary>
/// Counts tokens with the o200k_base encoding. Claude's tokenizer is not public, so absolute
/// numbers differ from what Claude bills, but the encoding is identical for every server under
/// test, which keeps the comparison fair.
/// </summary>
public static class TokenCounter
{
    private static readonly Tokenizer Tokenizer = TiktokenTokenizer.CreateForEncoding("o200k_base");

    public const string EncodingName = "o200k_base";

    public static int Count(string text) => string.IsNullOrEmpty(text) ? 0 : Tokenizer.CountTokens(text);

    /// <summary>
    /// Approximates the token cost of an image as Anthropic documents it: width * height / 750.
    /// </summary>
    public static int CountImage(int width, int height) => (int)Math.Ceiling(width * height / 750.0);
}
