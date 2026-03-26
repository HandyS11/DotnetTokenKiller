namespace DotnetTokenKiller.Domain.Filters;

/// <summary>Keyed service keys for <see cref="IOutputFilter"/> registrations.</summary>
public static class FilterKeys
{
    /// <summary>Key for the <c>dotnet build</c> output filter.</summary>
    public const string Build = "build";

    /// <summary>Key for the <c>dotnet test</c> output filter.</summary>
    public const string Test = "test";

    /// <summary>Key for the <c>dotnet restore</c> output filter.</summary>
    public const string Restore = "restore";

    /// <summary>Key for the <c>dotnet clean</c> output filter.</summary>
    public const string Clean = "clean";

    /// <summary>Key for the <c>dotnet format</c> output filter.</summary>
    public const string Format = "format";
}
