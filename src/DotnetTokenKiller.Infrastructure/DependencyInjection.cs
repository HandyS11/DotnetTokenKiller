using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using DotnetTokenKiller.Infrastructure.Configuration;
using DotnetTokenKiller.Infrastructure.Execution;
using DotnetTokenKiller.Infrastructure.Tee;
using DotnetTokenKiller.Infrastructure.Tracking;
using Microsoft.Extensions.DependencyInjection;

namespace DotnetTokenKiller.Infrastructure;

/// <summary>DI registration for Infrastructure layer services.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers infrastructure implementations.</summary>
    /// <param name="services">The service collection to add registrations to.</param>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<ICommandRunner, ProcessCommandRunner>();
        var dbPath = Environment.GetEnvironmentVariable("DTK_DB_PATH")
                     ?? SqliteTracker.GetDefaultDbPath();
        services.AddSingleton<ITracker>(_ => new SqliteTracker($"Data Source={dbPath}"));
        services.AddSingleton<IConfigProvider, JsonConfigProvider>();
        services.AddSingleton<ITeeService, FileTeeService>();
        return services;
    }
}
