using NUnit.Framework;

namespace SampleApp.Tests.NUnit;

[TestFixture]
public class FailingTests
{
    [Test]
    public void Equality_Fails()
    {
        Assert.That(42, Is.EqualTo(99), "Values should match but they don't");
    }

    [Test]
    public void Collection_Should_Contain_Missing_Item()
    {
        var list = new List<string> { "alpha", "beta", "gamma" };
        Assert.That(list, Does.Contain("delta"));
    }

    [Test]
    public void Unexpected_Exception()
    {
        throw new InvalidOperationException("Something went wrong in the test");
    }
}
