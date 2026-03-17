namespace SampleApp.MultiError;

/// <summary>Demonstrates access-level errors (CS0122).</summary>
public class AccessErrors
{
    public void TryAccess()
    {
        var secret = new SecretHolder();
        // CS0122 — 'SecretHolder._value' is inaccessible due to its protection level
        Console.WriteLine(secret._value);
    }
}

/// <summary>Helper class with a private field.</summary>
public class SecretHolder
{
    private readonly string _value = "hidden";
}
