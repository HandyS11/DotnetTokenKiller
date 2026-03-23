using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Application.UseCases;
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
        services.AddTransient<FilteredRunUseCase>();
        services.AddTransient<GainReportUseCase>();
        services.AddTransient<ResetTrackingUseCase>();
        services.AddTransient<ConfigSetUseCase>();
        services.AddTransient<DoctorUseCase>();
        services.AddTransient<DotnetBuildFilter>();
        services.AddTransient<DotnetTestFilter>();
        services.AddTransient<DotnetRestoreFilter>();
        services.AddTransient<DotnetCleanFilter>();
        services.AddSingleton<TextWriter>(_ => Console.Out);

        services.AddTransient<IProviderIntegrator, ClaudeCodeIntegrator>();
        services.AddTransient<IProviderIntegrator, GitHubCopilotIntegrator>();
        services.AddTransient<IProviderIntegrator, GeminiCliIntegrator>();
        services.AddTransient<IProviderIntegrator, CursorIntegrator>();
        services.AddTransient<IProviderIntegrator, WindsurfIntegrator>();
        services.AddTransient<IProviderIntegrator, AiderIntegrator>();
        services.AddTransient<IProviderIntegrator, JetBrainsAiIntegrator>();
        services.AddTransient<IntegrateUseCase>();

        return services;
    }
}
