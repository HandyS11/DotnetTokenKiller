namespace SampleApp.Warnings;

/// <summary>Demonstrates unused-variable and unreachable-code warnings.</summary>
public class UnusedCode
{
    public void DoWork()
    {
        // CS0168 — Variable is declared but never used
        int x;

        // CS0219 — Variable is assigned but its value is never used
        var y = "never read";

        Console.WriteLine("Work done");
    }

    public int DeadCode()
    {
        return 42;

        // CS0162 — Unreachable code detected
        Console.WriteLine("This never runs");
    }
}
