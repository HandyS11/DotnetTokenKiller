using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Integration;
using Microsoft.Extensions.DependencyInjection;

namespace DotnetTokenKiller.Application;

/// <summary>DI registration for Application layer services.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers use-cases and filters.</summary>
    /// <param name="services">The service collection to add registrations to.</param>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddTransient<FilteredOutputPipeline>();
        services.AddTransient<FilteredRunUseCase>();
        services.AddTransient<GainReportUseCase>();
        services.AddTransient<ResetTrackingUseCase>();
        services.AddTransient<FullResetUseCase>();
        services.AddTransient<ConfigSetUseCase>();
        services.AddTransient<DoctorUseCase>();
        services.AddKeyedTransient<IOutputFilter, DotnetBuildFilter>(FilterKeys.Build);
        services.AddKeyedTransient<IOutputFilter, DotnetTestFilter>(FilterKeys.Test);
        services.AddKeyedTransient<IOutputFilter, DotnetRestoreFilter>(FilterKeys.Restore);
        services.AddKeyedTransient<IOutputFilter, DotnetCleanFilter>(FilterKeys.Clean);
        services.AddKeyedTransient<IOutputFilter, DotnetFormatFilter>(FilterKeys.Format);
        services.AddKeyedTransient<IOutputFilter, DotnetListPackageFilter>(FilterKeys.ListPackage);
        services.AddSingleton<TextWriter>(_ => Console.Out);

        services.AddTransient<RtkHookCoexistence>();
        services.AddSingleton<HomePaths>();
        services.AddTransient<IProviderIntegrator, ClaudeCodeIntegrator>();
        services.AddTransient<IProviderIntegrator, GitHubCopilotIntegrator>();
        services.AddTransient<IProviderIntegrator, CopilotCliIntegrator>();
        services.AddTransient<IProviderIntegrator, GeminiCliIntegrator>();
        services.AddTransient<IProviderIntegrator, CursorIntegrator>();
        services.AddTransient<IProviderIntegrator, WindsurfIntegrator>();
        services.AddTransient<IProviderIntegrator, AiderIntegrator>();
        services.AddTransient<IProviderIntegrator, JetBrainsAiIntegrator>();
        services.AddTransient<IntegrateUseCase>();

        return services;
    }
}
