namespace DotnetTokenKiller.Domain.Filters;

/// <summary>
/// Keyed service keys for <see cref="IOutputFilter"/> registrations. Each key aliases the
/// corresponding <see cref="DotnetSubcommands"/> constant, so a key and its subcommand cannot
/// hold different values.
/// </summary>
public static class FilterKeys
{
    /// <summary>Key for the <c>dotnet build</c> output filter.</summary>
    public const string Build = DotnetSubcommands.Build;

    /// <summary>Key for the <c>dotnet test</c> output filter.</summary>
    public const string Test = DotnetSubcommands.Test;

    /// <summary>Key for the <c>dotnet restore</c> output filter.</summary>
    public const string Restore = DotnetSubcommands.Restore;

    /// <summary>Key for the <c>dotnet clean</c> output filter.</summary>
    public const string Clean = DotnetSubcommands.Clean;

    /// <summary>Key for the <c>dotnet format</c> output filter.</summary>
    public const string Format = DotnetSubcommands.Format;
}
