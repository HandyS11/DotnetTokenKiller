using Xunit;

namespace SampleApp.Tests.MultiFailure;

/// <summary>Tests that pass — mixed in to show the pass/fail ratio in dtk output.</summary>
public class PassingTests
{
    [Fact]
    public void Simple_Addition()
    {
        Assert.Equal(4, 2 + 2);
    }

    [Fact]
    public void String_Not_Empty()
    {
        Assert.NotEmpty("value");
    }

    [Fact]
    public void List_Has_Expected_Count()
    {
        var list = new List<int> { 1, 2, 3 };
        Assert.Equal(3, list.Count);
    }
}
