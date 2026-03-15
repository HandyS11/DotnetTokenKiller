using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace DotnetTokenKiller.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddTransient<FilteredRunUseCase>();
        services.AddTransient<GainReportUseCase>();
        services.AddSingleton<DotnetBuildFilter>();
        services.AddSingleton<DotnetTestFilter>();
        services.AddSingleton<DotnetRestoreFilter>();
        services.AddSingleton<DotnetCleanFilter>();
        return services;
    }
}
