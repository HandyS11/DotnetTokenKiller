using Xunit;

namespace SampleApp.Tests.MultiFailure;

/// <summary>Data-driven tests where some data rows pass and others fail.</summary>
public class DataDrivenFailures
{
    [Theory]
    [InlineData(2, 4, true)]   // pass
    [InlineData(3, 9, true)]   // pass
    [InlineData(5, 24, true)]  // FAIL — 5*5 = 25, not 24
    [InlineData(7, 49, true)]  // pass
    [InlineData(10, 99, true)] // FAIL — 10*10 = 100, not 99
    public void Square_Matches_Expected(int input, int expected, bool shouldPass)
    {
        _ = shouldPass; // marker parameter for readability
        Assert.Equal(expected, input * input);
    }

    [Theory]
    [InlineData("hello", 5)]
    [InlineData("world", 5)]
    [InlineData("", 1)]        // FAIL — empty string has length 0, not 1
    [InlineData("test", 4)]
    public void String_Length_Matches(string input, int expected)
    {
        Assert.Equal(expected, input.Length);
    }
}
