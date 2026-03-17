namespace SampleApp.Warnings;

/// <summary>Demonstrates nullable reference type warnings (CS8600, CS8602, CS8603).</summary>
public class NullableWarnings
{
    // CS8600 — Converting null literal or possible null value to non-nullable type
    public string GetValue()
    {
        string? maybeNull = null;
        string definite = maybeNull; // CS8600
        return definite;
    }

    // CS8602 — Dereference of a possibly null reference
    public int GetLength(string? input)
    {
        return input.Length; // CS8602
    }

    // CS8603 — Possible null reference return
    public string NeverNull()
    {
        string? data = null;
        return data; // CS8603
    }
}
