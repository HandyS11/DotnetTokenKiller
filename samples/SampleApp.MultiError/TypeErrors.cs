namespace SampleApp.MultiError;

/// <summary>Demonstrates type-conversion compiler errors (CS0029, CS0266).</summary>
public class TypeErrors
{
    // CS0029 — Cannot implicitly convert type 'string' to 'int'
    public int Code { get; } = "not-a-number";

    // CS0266 — Cannot implicitly convert type 'double' to 'int'
    public int Truncated { get; } = 3.14;
}
