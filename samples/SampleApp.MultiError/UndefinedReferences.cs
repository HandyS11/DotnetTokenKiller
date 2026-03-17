namespace SampleApp.MultiError;

/// <summary>Demonstrates undefined-name errors (CS0103).</summary>
public class UndefinedReferences
{
    public void DoWork()
    {
        // CS0103 — The name 'undeclaredVariable' does not exist in the current context
        Console.WriteLine(undeclaredVariable);

        // CS0103 — The name 'MissingMethod' does not exist in the current context
        MissingMethod();
    }
}
