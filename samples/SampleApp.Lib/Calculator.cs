namespace SampleApp.Lib;

/// <summary>A simple calculator used as a shared library.</summary>
public static class Calculator
{
    /// <summary>Adds two integers.</summary>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    public static int Add(int a, int b) => a + b;

    /// <summary>Subtracts <paramref name="b"/> from <paramref name="a"/>.</summary>
    /// <param name="a">The value to subtract from.</param>
    /// <param name="b">The value to subtract.</param>
    public static int Subtract(int a, int b) => a - b;

    /// <summary>Multiplies two integers.</summary>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    public static int Multiply(int a, int b) => a * b;

    /// <summary>Divides <paramref name="a"/> by <paramref name="b"/>.</summary>
    /// <param name="a">The dividend.</param>
    /// <param name="b">The divisor.</param>
    /// <exception cref="DivideByZeroException">Thrown when <paramref name="b"/> is zero.</exception>
    public static int Divide(int a, int b) => a / b;
}
