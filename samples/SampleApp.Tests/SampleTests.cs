using FluentAssertions;
using Xunit;

namespace SampleApp.Tests;

public class SampleTests
{
    [Fact]
    public void Addition_ReturnsCorrectResult()
    {
        const int result = 1 + 1;
        result.Should().Be(2);
    }

    [Fact]
    public void String_IsNotEmpty()
    {
        const string value = "Hello from SampleApp!";
        value.Should().NotBeEmpty();
    }

    [Fact]
    public void List_ContainsExpectedItems()
    {
        var list = new List<int> { 1, 2, 3 };
        list.Should().HaveCount(3);
    }
}
