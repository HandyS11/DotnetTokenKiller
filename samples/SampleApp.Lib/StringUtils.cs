namespace SampleApp.Lib;

/// <summary>A simple string utility used as a shared library.</summary>
public static class StringUtils
{
    /// <summary>Reverses the given string.</summary>
    /// <param name="input">The string to reverse.</param>
    public static string Reverse(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var chars = input.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }

    /// <summary>Returns whether the string is a palindrome.</summary>
    /// <param name="input">The string to check.</param>
    public static bool IsPalindrome(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var normalized = input.ToUpperInvariant();
        return normalized == Reverse(normalized);
    }
}
