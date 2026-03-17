using Reqnroll;
using Xunit;

namespace SampleApp.Tests.Reqnroll.StepDefinitions;

[Binding]
public class StringSteps
{
    private string _current = string.Empty;
    private string _result = string.Empty;

    [Given("the string {string}")]
    public void GivenTheString(string value) => _current = value;

    [When("{string} is appended")]
    public void WhenAppended(string suffix) => _result = _current + suffix;

    [When("the string is converted to uppercase")]
    public void WhenUppercase() => _result = _current.ToUpperInvariant();

    [When("the string is reversed")]
    public void WhenReversed()
    {
        var chars = _current.ToCharArray();
        Array.Reverse(chars);
        _result = new string(chars);
    }

    [Then("the result string should be {string}")]
    public void ThenTheResultStringShouldBe(string expected)
    {
        Assert.Equal(expected, _result);
    }
}
