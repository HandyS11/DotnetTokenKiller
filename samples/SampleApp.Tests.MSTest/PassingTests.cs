using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace SampleApp.Tests.MSTest;

[TestClass]
public class PassingTests
{
    [TestMethod]
    public void Addition_Returns_Correct_Result()
    {
        Assert.AreEqual(2, 1 + 1);
    }

    [TestMethod]
    public void String_StartsWith_Expected_Prefix()
    {
        Assert.IsTrue("Hello, World!".StartsWith("Hello", StringComparison.Ordinal));
    }

    [DataTestMethod]
    [DataRow(1, 1, 2)]
    [DataRow(5, 3, 8)]
    [DataRow(-1, 1, 0)]
    public void Add_Returns_Sum(int a, int b, int expected)
    {
        Assert.AreEqual(expected, a + b);
    }
}
