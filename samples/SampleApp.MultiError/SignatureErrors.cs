namespace SampleApp.MultiError;

/// <summary>Demonstrates method-signature errors (CS1501, CS1503).</summary>
public class SignatureErrors
{
    public void CallWrong()
    {
        // CS1501 — No overload for method 'Add' takes 3 arguments
        Add(1, 2, 3);

        // CS1503 — Argument 1: cannot convert from 'string' to 'int'
        Add("hello", 5);
    }

    private static int Add(int a, int b) => a + b;
}
