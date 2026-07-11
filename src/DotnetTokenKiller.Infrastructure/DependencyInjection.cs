using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using DotnetTokenKiller.Infrastructure.Configuration;
using DotnetTokenKiller.Infrastructure.Execution;
using DotnetTokenKiller.Infrastructure.Tee;
using DotnetTokenKiller.Infrastructure.Tracking;
using Microsoft.Data.Sqlite;
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
        services.AddSingleton<IConfigProvider, JsonConfigProvider>();

        services.AddSingleton<ITracker>(sp =>
        {
            var configProvider = sp.GetRequiredService<IConfigProvider>();
            var config = configProvider.Load();
            var dbPath = EnvironmentOverride.Read("DTK_DB_PATH")
                         ?? config.Tracking.DbPath
                         ?? SqliteTracker.GetDefaultDbPath();
            var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
            return new SqliteTracker(connectionString, config.Tracking.RetentionDays);
        });

        services.AddSingleton<ITeeService, FileTeeService>();
        return services;
    }
}
