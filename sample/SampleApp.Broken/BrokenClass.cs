namespace SampleApp.Broken;

public class BrokenClass
{
    public static void Run()
    {
        int x = "not an int"; // CS0029 — cannot implicitly convert type 'string' to 'int'
    }
}
