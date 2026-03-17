using Xunit;

namespace SampleApp.Tests.MultiFailure;

/// <summary>Tests that throw unexpected exceptions.</summary>
public class ExceptionTests
{
    [Fact]
    public void Throws_InvalidOperation()
    {
        throw new InvalidOperationException("Simulated invalid-operation during test");
    }

    [Fact]
    public void Throws_ArgumentNull()
    {
        throw new ArgumentNullException("param", "Simulated null argument");
    }

    [Fact]
    public void Throws_NotImplemented()
    {
        throw new NotImplementedException("Feature not yet implemented");
    }

    [Fact]
    public void Index_Out_Of_Range()
    {
        var list = new List<int> { 1, 2, 3 };
        _ = list[10]; // IndexOutOfRangeException / ArgumentOutOfRangeException
    }
}
