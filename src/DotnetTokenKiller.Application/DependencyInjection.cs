using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace DotnetTokenKiller.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddTransient<FilteredRunUseCase>();
        services.AddSingleton<DotnetBuildFilter>();
        services.AddSingleton<DotnetTestFilter>();
        services.AddSingleton<DotnetRestoreFilter>();
        services.AddSingleton<DotnetPublishFilter>();
        services.AddSingleton<DotnetPackFilter>();
        services.AddSingleton<DotnetCleanFilter>();
        services.AddSingleton<DotnetRunFilter>();
        services.AddSingleton<DotnetEfFilter>();
        return services;
    }
}
