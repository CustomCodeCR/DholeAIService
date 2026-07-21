using CustomCodeFramework.Postgres.DependencyInjection;
using CustomCodeFramework.Postgres.EntityFramework.DependencyInjection;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Messaging;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Persistence.Auditing;
using Dhole.AI.Persistence.DbContexts;
using Dhole.AI.Persistence.Messaging;
using Dhole.AI.Persistence.Initializations;
using Dhole.AI.Persistence.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dhole.AI.Persistence.DependencyInjection;

public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddCustomCodePostgres(configuration);

        services.AddCustomCodePostgresEntityFramework<ServiceDbContext>();

        services.AddScoped<IAiConnectionRepository, AiConnectionRepository>();

        services.AddScoped<IAiModelRepository, AiModelRepository>();

        services.AddScoped<IAiProfileRepository, AiProfileRepository>();

        services.AddScoped<IAiPromptTemplateRepository, AiPromptTemplateRepository>();

        services.AddScoped<IAiExecutionRepository, AiExecutionRepository>();

        services.AddScoped<IIntegrationEventOutboxWriter, IntegrationEventOutboxWriter>();

        services.AddScoped<IAiAuditService, AiAuditService>();

        services.AddScoped<AiDefaultProfilesInitializer>();

        return services;
    }
}
