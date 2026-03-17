using NUnit.Framework;

namespace SampleApp.Tests.NUnit;

[TestFixture]
public class PassingTests
{
    [Test]
    public void Addition_Returns_Correct_Result()
    {
        Assert.That(1 + 1, Is.EqualTo(2));
    }

    [Test]
    public void String_Contains_Substring()
    {
        Assert.That("Hello, World!", Does.Contain("World"));
    }

    [TestCase(2, 4)]
    [TestCase(3, 9)]
    [TestCase(4, 16)]
    public void Square_Returns_Expected_Value(int input, int expected)
    {
        Assert.That(input * input, Is.EqualTo(expected));
    }
}
