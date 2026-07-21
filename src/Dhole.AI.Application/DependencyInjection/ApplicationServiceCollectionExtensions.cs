using CustomCodeFramework.Cqrs.DependencyInjection;
using CustomCodeFramework.Validation.DependencyInjection;
using Dhole.AI.Application.Abstractions.Services;
using Dhole.AI.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Dhole.AI.Application.DependencyInjection;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddCustomCodeValidation(AssemblyReference.Assembly);

        services.AddCustomCodeCqrs(AssemblyReference.Assembly);

        services.AddCustomCodeCqrsBehaviors();

        services.AddScoped<IAiModelSelector, AiModelSelector>();

        services.AddScoped<IAiPromptCompiler, AiPromptCompiler>();

        services.AddScoped<IAiStructuredResponseValidator, AiStructuredResponseValidator>();

        services.AddScoped<IAiExecutionOrchestrator, AiExecutionOrchestrator>();

        return services;
    }
}
