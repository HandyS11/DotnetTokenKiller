namespace SampleApp.MultiError;

/// <summary>Demonstrates return-type and interface implementation errors.</summary>
public class MissingTypes
{
    // CS0029 — Cannot implicitly convert type 'string' to 'bool'
    public bool IsValid { get; } = "yes";

    // CS0029 — Cannot implicitly convert type 'long' to 'int'
    public int SmallNumber { get; } = 9999999999L;
}
