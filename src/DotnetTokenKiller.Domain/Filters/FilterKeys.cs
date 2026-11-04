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

    /// <summary>Key for the <c>dotnet list package</c> output filter.</summary>
    public const string ListPackage = DotnetSubcommands.ListPackage;

    /// <summary>Key for the <c>dotnet publish</c> output filter.</summary>
    public const string Publish = DotnetSubcommands.Publish;

    /// <summary>Key for the <c>dotnet pack</c> output filter.</summary>
    public const string Pack = DotnetSubcommands.Pack;

    /// <summary>Key for the <c>dotnet tool restore</c> output filter.</summary>
    public const string ToolRestore = DotnetSubcommands.ToolRestore;

    /// <summary>Key for the <c>dotnet ef migrations</c> output filter, also <see cref="EfDatabase"/>'s.</summary>
    public const string EfMigrations = DotnetSubcommands.EfMigrations;

    /// <summary>Key for the <c>dotnet ef database</c> output filter, also <see cref="EfMigrations"/>'s.</summary>
    public const string EfDatabase = DotnetSubcommands.EfDatabase;

    /// <summary>Key for the <c>dotnet stryker</c> output filter.</summary>
    public const string Stryker = DotnetSubcommands.Stryker;

    /// <summary>Key for the <c>dotnet jb cleanupcode</c> output filter.</summary>
    public const string JbCleanupCode = DotnetSubcommands.JbCleanupCode;
}
