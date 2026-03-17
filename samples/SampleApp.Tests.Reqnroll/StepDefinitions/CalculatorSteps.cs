using Reqnroll;
using Xunit;

namespace SampleApp.Tests.Reqnroll.StepDefinitions;

[Binding]
public class CalculatorSteps
{
    private int _first;
    private int _second;
    private int _result;

    [Given("the first number is {int}")]
    public void GivenTheFirstNumberIs(int number) => _first = number;

    [Given("the second number is {int}")]
    public void GivenTheSecondNumberIs(int number) => _second = number;

    [When("the two numbers are added")]
    public void WhenTheTwoNumbersAreAdded() => _result = _first + _second;

    [When("the second number is subtracted from the first")]
    public void WhenSubtracted() => _result = _first - _second;

    [When("the two numbers are multiplied")]
    public void WhenMultiplied() => _result = _first * _second;

    [When("the first number is divided by the second")]
    public void WhenDivided()
    {
        // Intentional: will throw DivideByZeroException for the "Division by zero" scenario
        _result = _first / _second;
    }

    [Then("the result should be {int}")]
    public void ThenTheResultShouldBe(int expected)
    {
        Assert.Equal(expected, _result);
    }
}
