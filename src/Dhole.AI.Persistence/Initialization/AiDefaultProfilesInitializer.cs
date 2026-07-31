using System.Globalization;
using System.Text.RegularExpressions;
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
              "maxItems": 100,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "pol": {
                    "type": ["string", "null"],
                    "description": "POL / origin / port of loading."
                  },
                  "poe": {
                    "type": ["string", "null"],
                    "description": "POE. Destination Port, Destination, Port of Discharge, arrival seaport or gateway belong here."
                  },
                  "pod": {
                    "type": ["string", "null"],
                    "description": "POD only when explicitly identified as POD, Place of Delivery or Final Destination. Never copy POE here."
                  },
                  "containerType": { "type": ["string", "null"] },
                  "carrier": { "type": ["string", "null"] },
                  "agent": { "type": ["string", "null"] },
                  "commodity": { "type": ["string", "null"] },
                  "currency": {
                    "type": "string",
                    "minLength": 3,
                    "maxLength": 3,
                    "description": "ISO 4217 currency code. USD is mandatory when the source does not explicitly prove another currency."
                  },
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
                  "pol", "poe", "pod", "containerType", "carrier", "agent",
                  "commodity", "currency", "freeDays", "transitDays",
                  "validFrom", "validTo", "oceanFreight", "originCharges",
                  "destinationCharges", "surcharges", "totalCost", "totalSale",
                  "profit", "margin", "spaceComment", "remarks"
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
                Eres el asistente de Dhole para logística, comercio exterior, aduanas, pricing, operaciones y soporte del sistema.
                Responde en español salvo petición contraria. Sé breve, preciso y accionable.
                No inventes datos, tarifas, regulaciones ni estados del sistema. Indica los datos faltantes y separa hechos, supuestos y recomendaciones.
                No reveles secretos ni información sensible. En decisiones comerciales o regulatorias, explica el riesgo y recomienda validación humana.
                """,
            RoutingMode: AiRoutingMode.LocalFirst,
            ResponseFormat: AiResponseFormat.Text,
            Temperature: 0.35m,
            MaximumOutputTokens: 1_024,
            TimeoutSeconds: 900,
            JsonSchema: null,
            RequiredCapability: AiModelCapability.Chat,
            ModelPreference: DefaultModelPreference.LocalFirst,
            EnforceConfiguration: true
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
                - Extrae solo valores explícitos; nunca inventes puertos, naviera, agente, fechas, montos, días libres o tránsito.
                - currency es obligatorio en todas las filas. La moneda por defecto es USD y usarla cuando no existe otra divisa explícita es una regla de negocio, no una invención. Solo devuelve otra moneda cuando la fuente la indique mediante código, nombre o símbolo inequívoco.
                - Usa previousExtraction como borrador, pero vuelve a comprobar cada dato contra sourceContent o la imagen adjunta. Corrige columnas, filas combinadas, fechas, montos y equipos antes de responder.
                - Repara caracteres dañados por OCR o codificación: MO�N debe interpretarse como MOIN cuando Config contiene Moín; PUERTO CORT�S como Puerto Cortés; MoÃ­n como Moín. Un carácter U+FFFD representa una posición desconocida, no una letra que deba eliminarse.
                - catalogHints contiene nombres reales de Config. Cuando exista una coincidencia inequívoca, devuelve exactamente el name canónico de Config; nunca uses recuerdos externos ni inventes un catálogo.
                - En nombres compuestos prioriza el nombre principal: TIANJIN (XINGANG) corresponde a Tianjin; YANTIAN (SHENZHEN) corresponde a Yantian/Shenzhen. No elijas el alias entre paréntesis si el nombre principal existe en Config.
                - Los sufijos legales S.A., S.A.S., Ltda., LLC, Inc. o equivalentes no cambian la identidad de un agente, pero no aceptes coincidencias parciales que puedan referirse a otra empresa.
                - Los grupos de Config son siempre: carriers, pol, pod, poe, currencies, agents, container-types y pricing-imports-profiles.
                - Cada combinación de ruta y contenedor produce una fila independiente.
                - Usa exactamente estos nombres en cada fila: pol, poe, pod, containerType, carrier, agent, commodity, currency, freeDays, transitDays, validFrom, validTo, oceanFreight, originCharges, destinationCharges, surcharges, totalCost, totalSale, profit, margin, spaceComment y remarks. No traduzcas ni cambies los nombres.
                - pol es el puerto de origen o Port of Loading.
                - poe es el puerto marítimo de destino/entrada. Cualquier etiqueta Destination, Destination Port, Puerto destino, Port of Discharge, Arrival Port, Gateway o POE se guarda en poe.
                - pod es otro destino: solo se llena cuando la fuente indica explícitamente POD, Place of Delivery, Delivery Place o Final Destination.
                - Nunca copies poe en pod ni deduzcas pod a partir de poe. Si no existe un POD explícito, devuelve pod=null.
                - Normaliza contenedores únicamente cuando sea claro: 20GP, 40GP, 40HC o 45HC.
                - Usa moneda ISO y fechas YYYY-MM-DD cuando puedan determinarse. Si no hay evidencia de otra divisa, currency debe ser "USD".
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
            MaximumOutputTokens: 768,
            TimeoutSeconds: 120,
            JsonSchema: PricingEmailJsonSchema,
            RequiredCapability: AiModelCapability.StructuredOutput,
            ModelPreference: DefaultModelPreference.FastStructured,
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
            TimeoutSeconds: 900,
            JsonSchema: null,
            RequiredCapability: AiModelCapability.Chat,
            ModelPreference: DefaultModelPreference.AnalysisQuality,
            EnforceConfiguration: true
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
            ReadPositiveInt(configuration["AI:DefaultProfiles:MaximumModelsPerProfile"], 3),
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

            var visionCompatibleModels = definition.Key == "pricing-email-analysis"
                ? availableModels
                    .Where(model => model.Supports(
                        definition.RequiredCapability | AiModelCapability.Vision
                    ))
                    .ToArray()
                : [];
            var visionCompatibleModelIds = visionCompatibleModels
                .Select(model => model.Id)
                .ToHashSet();
            var shouldConfigureVisionFallback = visionCompatibleModels.Length > 0;
            var hasVisionConfiguredModel = profile.Models.Any(model =>
                visionCompatibleModelIds.Contains(model.ModelId)
            );

            if (
                profile.Models.Count == 0
                || !hasCompatibleConfiguredModel
                || definition.EnforceConfiguration
                || (shouldConfigureVisionFallback && !hasVisionConfiguredModel)
            )
            {
                var selectedModels = SelectModels(
                    compatibleModels,
                    definition.ModelPreference,
                    maximumModels
                ).ToList();

                if (shouldConfigureVisionFallback)
                {
                    var visionModel = SelectModels(
                        visionCompatibleModels,
                        definition.ModelPreference,
                        1
                    ).First();

                    if (selectedModels.All(model => model.Id != visionModel.Id))
                    {
                        if (selectedModels.Count >= maximumModels)
                        {
                            selectedModels.RemoveAt(selectedModels.Count - 1);
                        }

                        selectedModels.Add(visionModel);
                    }
                }

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
            DefaultModelPreference.FastStructured => models
                .OrderByDescending(model => model.Status == AiModelStatus.Available)
                .ThenByDescending(model => model.Supports(AiModelCapability.StructuredOutput))
                .ThenByDescending(model => model.IsLocal)
                .ThenBy(GetStructuredExtractionModelRank)
                .ThenBy(GetStructuredModelGenerationRank)
                .ThenByDescending(model => model.ContextWindow ?? 0)
                .ThenBy(model => model.Name),

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

    private static int GetStructuredModelGenerationRank(AiModel model)
    {
        var identity = $"{model.ExternalModelId} {model.Name}".ToLowerInvariant();

        if (
            identity.Contains("llama3.2", StringComparison.Ordinal)
            || identity.Contains("llama3.1", StringComparison.Ordinal)
        )
        {
            return 0;
        }

        if (identity.Contains("mistral", StringComparison.Ordinal))
        {
            return 1;
        }

        return identity.Contains("llama3", StringComparison.Ordinal) ? 2 : 1;
    }

    private static int GetStructuredExtractionModelRank(AiModel model)
    {
        var identity = $"{model.ExternalModelId} {model.Name}";
        var match = Regex.Match(
            identity,
            @"(?<!\d)(?<size>\d+(?:\.\d+)?)\s*b(?![a-z])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );

        if (
            !match.Success
            || !decimal.TryParse(
                match.Groups["size"].Value,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var billions
            )
        )
        {
            return 1;
        }

        return billions switch
        {
            >= 7m and <= 14m => 0,
            < 7m => 2,
            _ => 3,
        };
    }

    private static bool ReadBoolean(string? value, bool fallback) =>
        bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static int ReadPositiveInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

    private enum DefaultModelPreference
    {
        LocalFirst = 1,
        AnalysisQuality = 2,
        FastStructured = 3,
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
