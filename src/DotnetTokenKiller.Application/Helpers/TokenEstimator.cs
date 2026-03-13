namespace DotnetTokenKiller.Application.Helpers;

public static class TokenEstimator
{
    public static int Estimate(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return text.Length / 4;
    }
}
