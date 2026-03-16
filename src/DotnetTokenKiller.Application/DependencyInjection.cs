using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace DotnetTokenKiller.Application;

/// <summary>DI registration for Application layer services.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers use-cases and filters.</summary>
    /// <param name="services">The service collection to add registrations to.</param>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddTransient<FilteredRunUseCase>();
        services.AddTransient<GainReportUseCase>();
        services.AddTransient<ResetTrackingUseCase>();
        services.AddSingleton<DotnetBuildFilter>();
        services.AddSingleton<DotnetTestFilter>();
        services.AddSingleton<DotnetRestoreFilter>();
        services.AddSingleton<DotnetCleanFilter>();
        return services;
    }
}
