using FluentAssertions;
using Xunit;

namespace SampleApp.Tests.MultiFailure;

/// <summary>Assertion failures of various kinds.</summary>
public class AssertionFailures
{
    [Fact]
    public void Equality_Mismatch()
    {
        const int actual = 42;
        actual.Should().Be(99, "because we want to show a numeric mismatch");
    }

    [Fact]
    public void String_Should_Match_But_Doesnt()
    {
        const string actual = "Hello World";
        actual.Should().Be("Hello Mars", "because this demonstrates string diff output");
    }

    [Fact]
    public void Collection_Missing_Element()
    {
        var list = new[] { "alpha", "beta", "gamma" };
        list.Should().Contain("delta", "because the collection was expected to include delta");
    }

    [Fact]
    public void Bool_Expected_True()
    {
        const bool condition = false;
        condition.Should().BeTrue("because a true flag was expected");
    }

    [Fact]
    public void Object_Should_Not_Be_Null()
    {
        const string? value = null;
        value.Should().NotBeNull("because a non-null value was expected");
    }
}
