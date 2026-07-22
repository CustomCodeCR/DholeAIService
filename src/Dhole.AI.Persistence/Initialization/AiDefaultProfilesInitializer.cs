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

    private const string PricingEmailJsonSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "success": { "type": "boolean" },
            "confidence": { "type": "number", "minimum": 0, "maximum": 100 },
            "rows": {
              "type": "array",
              "maxItems": 200,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "originPort": { "type": ["string", "null"] },
                  "portOfExit": { "type": ["string", "null"] },
                  "destinationPort": { "type": ["string", "null"] },
                  "containerType": { "type": ["string", "null"] },
                  "carrier": { "type": ["string", "null"] },
                  "agent": { "type": ["string", "null"] },
                  "commodity": { "type": ["string", "null"] },
                  "currency": { "type": ["string", "null"] },
                  "freeDays": { "type": ["integer", "null"], "minimum": 0 },
                  "transitDays": { "type": ["integer", "null"], "minimum": 0 },
                  "validFrom": { "type": ["string", "null"] },
                  "validTo": { "type": ["string", "null"] },
                  "oceanFreight": { "type": ["number", "null"] },
                  "originCharges": { "type": ["number", "null"] },
                  "destinationCharges": { "type": ["number", "null"] },
                  "surcharges": { "type": ["number", "null"] },
                  "totalCost": { "type": ["number", "null"] },
                  "totalSale": { "type": ["number", "null"] },
                  "profit": { "type": ["number", "null"] },
                  "margin": { "type": ["number", "null"] },
                  "spaceComment": { "type": ["string", "null"] },
                  "remarks": { "type": ["string", "null"] }
                },
                "required": [
                  "originPort", "destinationPort", "containerType",
                  "carrier", "currency", "oceanFreight"
                ]
              }
            },
            "warnings": {
              "type": "array",
              "items": { "type": "string" }
            }
          },
          "required": ["success", "confidence", "rows", "warnings"]
        }
        """;

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
            ResponseFormat: AiResponseFormat.Text,
            Temperature: 0.35m,
            MaximumOutputTokens: 2_048,
            TimeoutSeconds: 120,
            JsonSchema: null,
            RequiredCapability: AiModelCapability.Chat,
            ModelPreference: DefaultModelPreference.LocalFirst,
            EnforceConfiguration: false
        ),
        new(
            Key: "pricing-email-analysis",
            Name: "Extracción IA de correos de Pricing",
            Description: "Fallback estructurado para extraer tarifas FCL desde correos y sus adjuntos cuando DataExtraction no puede analizarlos.",
            TemplateKey: PricingEmailTemplateKey,
            TemplateName: "Extracción de correos de Pricing",
            TemplateDescription: "Instrucciones especializadas para convertir correos y adjuntos de tarifas FCL en filas estructuradas para DataExtraction y Pricing.",
            SystemPrompt: """
                Eres un extractor de tarifas FCL. Recibirás JSON con metadatos del correo, una tabla convertida a texto o el contenido textual de un adjunto y el resultado previo de DataExtraction.

                Devuelve únicamente el objeto JSON del esquema, sin markdown, explicaciones ni texto adicional.

                Reglas:
                - Extrae solo valores explícitos; nunca inventes puertos, naviera, agente, moneda, fechas, montos, días libres o tránsito.
                - Copia POL, POE, POD, naviera, agente, contenedor y moneda tal como aparecen en el correo o documento. No los traduzcas, completes ni reemplaces por nombres que recuerdes: DataExtraction será el único responsable de compararlos y estandarizarlos contra Config.
                - Cada combinación de ruta y contenedor produce una fila independiente.
                - Usa exactamente estos nombres en cada fila: originPort, portOfExit, destinationPort, containerType, carrier, agent, commodity, currency, freeDays, transitDays, validFrom, validTo, oceanFreight, originCharges, destinationCharges, surcharges, totalCost, totalSale, profit, margin, spaceComment y remarks. No traduzcas ni cambies los nombres.
                - originPort = POL, portOfExit = POE y destinationPort = POD.
                - Normaliza contenedores únicamente cuando sea claro: 20GP, 40GP, 40HC o 45HC.
                - Usa moneda ISO y fechas YYYY-MM-DD cuando puedan determinarse.
                - Todos los montos, días, margen y confianza deben ser números JSON sin símbolos de moneda, porcentajes ni separadores de miles.
                - oceanFreight es el flete marítimo por contenedor.
                - Para datos ausentes usa null cuando el esquema permita null; no inventes texto de relleno.
                - La raíz siempre debe ser {"success": true|false, "confidence": 0-100, "rows": [...], "warnings": [...]}. No devuelvas un arreglo directo ni envuelvas el objeto en data, result o content.
                - success=false y rows=[] cuando no haya evidencia suficiente de tarifas FCL.
                - confidence va de 0 a 100 y warnings contiene ambigüedades reales.
                """,
            RoutingMode: AiRoutingMode.PriorityFallback,
            ResponseFormat: AiResponseFormat.JsonSchema,
            Temperature: 0.05m,
            MaximumOutputTokens: 1_600,
            TimeoutSeconds: 240,
            JsonSchema: PricingEmailJsonSchema,
            RequiredCapability: AiModelCapability.StructuredOutput,
            ModelPreference: DefaultModelPreference.AnalysisQuality,
            EnforceConfiguration: true
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
            ResponseFormat: AiResponseFormat.Text,
            Temperature: 0.10m,
            MaximumOutputTokens: 3_500,
            TimeoutSeconds: 180,
            JsonSchema: null,
            RequiredCapability: AiModelCapability.Chat,
            ModelPreference: DefaultModelPreference.AnalysisQuality,
            EnforceConfiguration: false
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

        var availableModels = await GetAvailableModelsAsync(cancellationToken);
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
            else
            {
                if (definition.EnforceConfiguration)
                {
                    template.Update(
                        definition.TemplateKey,
                        definition.TemplateName,
                        definition.TemplateDescription,
                        definition.SystemPrompt,
                        null,
                        null,
                        null
                    );
                }

                if (!template.IsActive)
                {
                    template.Activate(null);
                }
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
                    definition.ResponseFormat,
                    definition.Temperature,
                    definition.MaximumOutputTokens,
                    definition.TimeoutSeconds,
                    definition.JsonSchema,
                    null
                );

                await dbContext.AiProfiles.AddAsync(profile, cancellationToken);
                profilesCreated++;
                createdNow = true;
            }
            else if (definition.EnforceConfiguration || (profile.Models.Count == 0 && !profile.IsActive))
            {
                profile.Update(
                    definition.Key,
                    definition.Name,
                    definition.Description,
                    template.Id,
                    definition.RoutingMode,
                    definition.ResponseFormat,
                    definition.Temperature,
                    definition.MaximumOutputTokens,
                    definition.TimeoutSeconds,
                    definition.JsonSchema,
                    null
                );
            }

            var compatibleModels = availableModels
                .Where(model => model.Supports(definition.RequiredCapability))
                .ToArray();
            var compatibleModelIds = compatibleModels.Select(model => model.Id).ToHashSet();
            var hasCompatibleConfiguredModel = profile.Models.Any(model =>
                compatibleModelIds.Contains(model.ModelId)
            );

            var hasIncompatibleConfiguredModel = profile.Models.Any(model =>
                !compatibleModelIds.Contains(model.ModelId)
            );

            if (
                profile.Models.Count == 0
                || !hasCompatibleConfiguredModel
                || (definition.EnforceConfiguration && hasIncompatibleConfiguredModel)
            )
            {
                var selectedModels = SelectModels(
                    compatibleModels,
                    definition.ModelPreference,
                    maximumModels
                );

                if (selectedModels.Count == 0)
                {
                    if (profile.IsActive)
                    {
                        profile.Inactivate(null);
                    }

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
                "Los perfiles predeterminados fueron creados, pero todavía falta un modelo activo con las capacidades requeridas para alguno de ellos."
            );
        }

        return new(
            allReady,
            templatesCreated,
            profilesCreated,
            profilesConfigured,
            profilesActivated,
            availableModels.Count
        );
    }

    private async Task<IReadOnlyCollection<AiModel>> GetAvailableModelsAsync(
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
        AiResponseFormat ResponseFormat,
        decimal Temperature,
        int MaximumOutputTokens,
        int TimeoutSeconds,
        string? JsonSchema,
        AiModelCapability RequiredCapability,
        DefaultModelPreference ModelPreference,
        bool EnforceConfiguration
    );
}
