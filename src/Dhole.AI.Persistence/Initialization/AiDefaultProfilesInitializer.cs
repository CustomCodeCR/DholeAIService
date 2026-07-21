using Dhole.AI.Domain.Models.Entities;
using Dhole.AI.Domain.Models.Enums;
using Dhole.AI.Domain.Profiles.Entities;
using Dhole.AI.Domain.Profiles.Enums;
using Dhole.AI.Domain.PromptTemplates.Entities;
using Dhole.AI.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dhole.AI.Persistence.Initializations;

public sealed record AiDefaultProfilesInitializationResult(
    bool IsReady,
    int TemplatesCreated,
    int ProfilesCreated,
    int ProfilesConfigured,
    int ProfilesActivated,
    int CompatibleModels
);

public sealed class AiDefaultProfilesInitializer(
    ServiceDbContext dbContext,
    IConfiguration configuration,
    ILogger<AiDefaultProfilesInitializer> logger
)
{
    private const string AssistantTemplateKey = "assistant";
    private const string PricingEmailTemplateKey = "pricing-email-analysis";
    private const string PricingDashboardTemplateKey = "pricing-dashboard-analysis";

    private static readonly DefaultProfileDefinition[] Definitions =
    [
        new(
            Key: "assistant",
            Name: "Asistente general de Dhole",
            Description: "Asistente conversacional general disponible desde Dhole Web.",
            TemplateKey: AssistantTemplateKey,
            TemplateName: "Asistente general de Dhole",
            TemplateDescription: "Instrucciones base para el asistente conversacional del ecosistema Dhole.",
            SystemPrompt: """
                Eres el asistente de inteligencia artificial del ecosistema Dhole. Ayuda con logística, comercio exterior, aduanas, pricing, operaciones y soporte funcional del sistema. Responde en español, salvo que el usuario solicite otro idioma. Sé directo, preciso y orientado a acciones. No inventes datos, tarifas, regulaciones, estados de procesos ni resultados del sistema. Cuando falte información, indica claramente qué dato falta. Distingue hechos, supuestos y recomendaciones. No reveles secretos, credenciales, tokens ni información sensible. Para decisiones comerciales o regulatorias, señala los riesgos y recomienda validación humana cuando corresponda.
                """,
            RoutingMode: AiRoutingMode.LocalFirst,
            Temperature: 0.35m,
            MaximumOutputTokens: 2_048,
            TimeoutSeconds: 120,
            ModelPreference: DefaultModelPreference.LocalFirst
        ),
        new(
            Key: "pricing-email-analysis",
            Name: "Análisis IA de correos de Pricing",
            Description: "Analiza correos, extracciones y lotes importados para Pricing FCL.",
            TemplateKey: PricingEmailTemplateKey,
            TemplateName: "Análisis de correos de Pricing",
            TemplateDescription: "Instrucciones especializadas para evaluar correos y datos extraídos de tarifarios FCL.",
            SystemPrompt: """
                Actúa como analista senior de Pricing FCL y comercio exterior. Recibirás datos de un correo, su extracción y, cuando exista, información del lote importado. Identifica y valida ruta, POL, POE, POD, naviera, agente, tipo y cantidad de contenedores, moneda, vigencia, días libres, tiempo de tránsito, flete marítimo, flete terrestre y cargos adicionales. Detecta campos faltantes, inconsistencias, duplicados, fechas vencidas, valores atípicos, baja confianza de extracción y cualquier condición que requiera revisión humana. Cuando haya varias alternativas, compáralas por costo total, vigencia, tránsito, confiabilidad de los datos y riesgos comerciales; no elijas únicamente el menor flete. Responde en español usando exactamente estas secciones: Resumen, Datos detectados, Alertas, Recomendación y Próxima acción. No inventes información ni completes valores ausentes por suposición.
                """,
            RoutingMode: AiRoutingMode.PriorityFallback,
            Temperature: 0.10m,
            MaximumOutputTokens: 3_000,
            TimeoutSeconds: 180,
            ModelPreference: DefaultModelPreference.AnalysisQuality
        ),
        new(
            Key: "pricing-dashboard-analysis",
            Name: "Análisis IA del panel de Pricing",
            Description: "Compara tarifas importadas y recomienda las mejores opciones del panel de Pricing.",
            TemplateKey: PricingDashboardTemplateKey,
            TemplateName: "Análisis del panel de Pricing",
            TemplateDescription: "Instrucciones especializadas para comparar tarifas FCL desde el dashboard.",
            SystemPrompt: """
                Actúa como analista senior de Pricing FCL. Recibirás las tarifas importadas correspondientes a los filtros del panel. Evalúa por separado las vías Limón/Moín, Puerto Caldera y Multimodal. Compara naviera, POL, POE, POD, tipo de contenedor, cantidad de contenedores, flete marítimo internacional, flete terrestre internacional, costos, venta, utilidad, margen, vigencia, días libres, tiempo de tránsito y calidad de los datos. Para rutas multimodales verifica el flete terrestre esperado de USD 2,140 y señala cualquier ausencia o diferencia. Recomienda las mejores alternativas considerando costo total, margen mínimo esperado del 12%, vigencia, tránsito, confiabilidad y datos faltantes; no selecciones una opción únicamente por tener el menor flete. Responde en español usando exactamente estas secciones: Resumen ejecutivo, Mejores opciones, Riesgos, Oportunidades de margen y Acciones recomendadas. No inventes datos ni presentes como aprobada una tarifa que no tenga evidencia de aprobación.
                """,
            RoutingMode: AiRoutingMode.PriorityFallback,
            Temperature: 0.10m,
            MaximumOutputTokens: 3_500,
            TimeoutSeconds: 180,
            ModelPreference: DefaultModelPreference.AnalysisQuality
        ),
    ];

    public async Task<AiDefaultProfilesInitializationResult> InitializeAsync(
        CancellationToken cancellationToken = default
    )
    {
        if (!ReadBoolean(configuration["AI:DefaultProfiles:Enabled"], true))
        {
            return new(true, 0, 0, 0, 0, 0);
        }

        var maximumModels = Math.Clamp(
            ReadPositiveInt(configuration["AI:DefaultProfiles:MaximumModelsPerProfile"], 5),
            1,
            20
        );

        var compatibleModels = await GetCompatibleModelsAsync(cancellationToken);
        var compatibleModelIds = compatibleModels.Select(model => model.Id).ToHashSet();
        var templatesCreated = 0;
        var profilesCreated = 0;
        var profilesConfigured = 0;
        var profilesActivated = 0;
        var allReady = true;

        foreach (var definition in Definitions)
        {
            var template = await dbContext.AiPromptTemplates.SingleOrDefaultAsync(
                item => item.Key == definition.TemplateKey && !item.IsDeleted,
                cancellationToken
            );

            if (template is null)
            {
                template = AiPromptTemplate.Create(
                    definition.TemplateKey,
                    definition.TemplateName,
                    definition.TemplateDescription,
                    definition.SystemPrompt,
                    null,
                    null,
                    null
                );

                await dbContext.AiPromptTemplates.AddAsync(template, cancellationToken);
                templatesCreated++;
            }
            else if (!template.IsActive)
            {
                template.Activate(null);
            }

            var profile = await dbContext.AiProfiles
                .Include(item => item.Models)
                .SingleOrDefaultAsync(
                    item => item.Key == definition.Key && !item.IsDeleted,
                    cancellationToken
                );

            var createdNow = false;

            if (profile is null)
            {
                profile = AiProfile.Create(
                    definition.Key,
                    definition.Name,
                    definition.Description,
                    template.Id,
                    definition.RoutingMode,
                    AiResponseFormat.Text,
                    definition.Temperature,
                    definition.MaximumOutputTokens,
                    definition.TimeoutSeconds,
                    null,
                    null
                );

                await dbContext.AiProfiles.AddAsync(profile, cancellationToken);
                profilesCreated++;
                createdNow = true;
            }
            else if (profile.Models.Count == 0 && !profile.IsActive)
            {
                profile.Update(
                    definition.Key,
                    definition.Name,
                    definition.Description,
                    template.Id,
                    definition.RoutingMode,
                    AiResponseFormat.Text,
                    definition.Temperature,
                    definition.MaximumOutputTokens,
                    definition.TimeoutSeconds,
                    null,
                    null
                );
            }

            var hasCompatibleConfiguredModel = profile.Models.Any(model =>
                compatibleModelIds.Contains(model.ModelId)
            );

            if (profile.Models.Count == 0 || !hasCompatibleConfiguredModel)
            {
                var selectedModels = SelectModels(
                    compatibleModels,
                    definition.ModelPreference,
                    maximumModels
                );

                if (selectedModels.Count == 0)
                {
                    allReady = false;
                    continue;
                }

                profile.ConfigureModels(
                    selectedModels.Select((model, index) => (
                        ModelId: model.Id,
                        Priority: index + 1,
                        IsFallback: index > 0
                    )),
                    null
                );

                profilesConfigured++;
            }

            if (!profile.IsActive)
            {
                profile.Activate(null);
                profilesActivated++;
            }

            if (createdNow)
            {
                logger.LogInformation(
                    "Perfil de IA predeterminado creado: {ProfileKey}.",
                    definition.Key
                );
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (!allReady)
        {
            logger.LogWarning(
                "Los perfiles predeterminados fueron creados, pero aún no existe un modelo activo con capacidad Chat para configurarlos."
            );
        }

        return new(
            allReady,
            templatesCreated,
            profilesCreated,
            profilesConfigured,
            profilesActivated,
            compatibleModels.Count
        );
    }

    private async Task<IReadOnlyCollection<AiModel>> GetCompatibleModelsAsync(
        CancellationToken cancellationToken
    )
    {
        return await (
            from model in dbContext.AiModels.AsNoTracking()
            join connection in dbContext.AiConnections.AsNoTracking()
                on model.ConnectionId equals connection.Id
            where
                !model.IsDeleted
                && model.IsActive
                && !connection.IsDeleted
                && connection.IsActive
                && (model.Capabilities & AiModelCapability.Chat) == AiModelCapability.Chat
            select model
        ).ToListAsync(cancellationToken);
    }

    private static IReadOnlyCollection<AiModel> SelectModels(
        IReadOnlyCollection<AiModel> models,
        DefaultModelPreference preference,
        int maximumModels
    )
    {
        IEnumerable<AiModel> ordered = preference switch
        {
            DefaultModelPreference.LocalFirst => models
                .OrderByDescending(model => model.Status == AiModelStatus.Available)
                .ThenByDescending(model => model.IsLocal)
                .ThenByDescending(model => model.ContextWindow ?? 0)
                .ThenByDescending(model => model.MaximumOutputTokens ?? 0)
                .ThenBy(model => model.Name),

            _ => models
                .OrderByDescending(model => model.Status == AiModelStatus.Available)
                .ThenByDescending(model => model.Supports(AiModelCapability.StructuredOutput))
                .ThenByDescending(model => model.ContextWindow ?? 0)
                .ThenByDescending(model => model.MaximumOutputTokens ?? 0)
                .ThenByDescending(model => model.IsLocal)
                .ThenBy(model => model.Name),
        };

        return ordered.Take(maximumModels).ToArray();
    }

    private static bool ReadBoolean(string? value, bool fallback) =>
        bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static int ReadPositiveInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

    private enum DefaultModelPreference
    {
        LocalFirst = 1,
        AnalysisQuality = 2,
    }

    private sealed record DefaultProfileDefinition(
        string Key,
        string Name,
        string Description,
        string TemplateKey,
        string TemplateName,
        string TemplateDescription,
        string SystemPrompt,
        AiRoutingMode RoutingMode,
        decimal Temperature,
        int MaximumOutputTokens,
        int TimeoutSeconds,
        DefaultModelPreference ModelPreference
    );
}
