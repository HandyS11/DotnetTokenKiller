using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SampleApp.Tests.MSTest;

[TestClass]
public class FailingTests
{
    [TestMethod]
    public void Equality_Fails()
    {
        Assert.AreEqual(100, 42, "Expected values to be equal");
    }

    [TestMethod]
    public void Null_Check_Fails()
    {
        const string value = "not null";
        Assert.IsNull(value, "Value was expected to be null");
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentException))]
    public void Wrong_Exception_Type()
    {
        // Throws InvalidOperationException instead of the expected ArgumentException
        throw new InvalidOperationException("Wrong exception type");
    }
}
