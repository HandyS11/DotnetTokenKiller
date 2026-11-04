namespace DotnetTokenKiller.Domain.Tee;

/// <summary>The file <c>--tee &lt;path&gt;</c> names cannot be created, so the run must not start.</summary>
/// <remarks>
/// Thrown by <see cref="IRawOutputCopyFactory.Open"/> with the message <c>&lt;path&gt;: &lt;reason&gt;</c>: the
/// path as the user gave it and the operating system's reason, which the run prints as its only line.
/// </remarks>
public sealed class RawOutputCopyException : Exception
{
    /// <summary>Initializes a new instance with a default message.</summary>
    public RawOutputCopyException()
    {
    }

    /// <summary>Initializes a new instance.</summary>
    /// <param name="message"><c>&lt;path&gt;: &lt;reason&gt;</c>.</param>
    public RawOutputCopyException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance wrapping the error that stopped the file being created.</summary>
    /// <param name="message"><c>&lt;path&gt;: &lt;reason&gt;</c>.</param>
    /// <param name="innerException">The error that stopped the file being created.</param>
    public RawOutputCopyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
