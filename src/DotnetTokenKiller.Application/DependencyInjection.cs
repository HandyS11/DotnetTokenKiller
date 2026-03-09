using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace DotnetTokenKiller.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddTransient<FilteredRunUseCase>();
        services.AddSingleton<DotnetBuildFilter>(_ => new DotnetBuildFilter());
        services.AddSingleton<DotnetTestFilter>(_ => new DotnetTestFilter());
        services.AddSingleton<DotnetRestoreFilter>(_ => new DotnetRestoreFilter());
        return services;
    }
}
