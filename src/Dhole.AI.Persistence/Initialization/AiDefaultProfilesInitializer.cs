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
                    "description": "POE. Destination Port, Port of Discharge, arrival seaport or gateway belong here. A POD header in an ocean-rate table also belongs here when it means Port of Discharge."
                  },
                  "pod": {
                    "type": ["string", "null"],
                    "description": "POD only for Place of Delivery, Delivery Place or Final Destination. Do not place an ocean-rate POD header here when it means Port of Discharge."
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
                  "oceanFreight": { "type": ["number", "null"], "minimum": -1000000000, "maximum": 1000000000 },
                  "originCharges": { "type": ["number", "null"], "minimum": -1000000000, "maximum": 1000000000 },
                  "destinationCharges": { "type": ["number", "null"], "minimum": -1000000000, "maximum": 1000000000 },
                  "surcharges": { "type": ["number", "null"], "minimum": -1000000000, "maximum": 1000000000 },
                  "totalCost": { "type": ["number", "null"], "minimum": -1000000000, "maximum": 1000000000 },
                  "totalSale": { "type": ["number", "null"], "minimum": -1000000000, "maximum": 1000000000 },
                  "profit": { "type": ["number", "null"], "minimum": -1000000000, "maximum": 1000000000 },
                  "margin": { "type": ["number", "null"], "minimum": -100000, "maximum": 100000 },
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
                - Cuando sourceContent contenga una cadena de respuestas, reenviados o correos citados, usa la primera sección visible que contenga una tarifa FCL completa como la oferta vigente. Nunca elijas una sección posterior solo porque tenga más filas, montos o detalle; esas secciones son historial.
                - Repara caracteres dañados por OCR o codificación: MO�N debe interpretarse como MOIN cuando Config contiene Moín; PUERTO CORT�S como Puerto Cortés; MoÃ­n como Moín. Un carácter U+FFFD representa una posición desconocida, no una letra que deba eliminarse.
                - catalogHints contiene nombres reales de Config. Cuando exista una coincidencia inequívoca, devuelve exactamente el name canónico de Config; nunca uses recuerdos externos ni inventes un catálogo.
                - En nombres compuestos prioriza el nombre principal: TIANJIN (XINGANG) corresponde a Tianjin; YANTIAN (SHENZHEN) corresponde a Yantian/Shenzhen. No elijas el alias entre paréntesis si el nombre principal existe en Config.
                - Para agent busca primero en el asunto y después en el cuerpo del correo. Solo usa un agente presente en catalogHints y únicamente cuando la coincidencia sea inequívoca. Tolera prefijos RE:/RV:/FWD:, separadores como // y una diferencia ortográfica mínima, por ejemplo CASTRO FALLS puede coincidir con Castro Fallas.
                - Los sufijos legales S.A., S.A.S., Ltda., LLC, Inc. o equivalentes no cambian la identidad de un agente, pero no aceptes coincidencias parciales que puedan referirse a otra empresa. No deduzcas agent únicamente por la dirección del remitente.
                - Los grupos de Config son siempre: carriers, pol, pod, poe, currencies, agents, container-types y pricing-imports-profiles.
                - Devuelve filas compactas. Cuando varios POL o puertos de descarga comparten exactamente carrier, equipo, mercancía, vigencia, flete y recargos, mantenlos unidos con / en una sola fila; DataExtraction expandirá después el producto cartesiano.
                - Separa filas cuando cambie carrier, containerType, commodity, oceanFreight u originCharges. Nunca unas varias navieras en la misma fila.
                - Extrae todas las tablas tarifarias del mensaje actual. No te detengas después de la primera tabla cuando el mismo correo incluya una segunda matriz con otras navieras o destinos.
                - Cuando una tabla tenga columnas de monto por equipo, cada monto corresponde a su encabezado. 20' es 20DV/20GP. Un encabezado compartido 40'/40HC aplica el mismo monto tanto a 40DV/40GP como a 40HC, por lo que debes devolver filas separadas para ambos equipos.
                - "Effective ETD" identifica una salida concreta, no un rango abierto. Cuando tenga una fecha, usa esa misma fecha en validFrom y validTo. Si el valor es OMIT/OMITTED/NO SAILING/CANCELLED, no devuelvas una tarifa utilizable para esa ruta y agrega una advertencia.
                - Cuando montos y navieras aparecen en listas paralelas, asócialos por posición. Ejemplo: USD6300/6400 junto a Carrier MSC/ONE significa MSC=6300 y ONE=6400, salvo evidencia explícita contraria.
                - En correos marítimos, una etiqueta POD junto a una lista de puertos suele significar Port of Discharge y se guarda en poe. Usa pod únicamente para Place of Delivery o Final Destination explícito.
                - Si el mensaje indica "Below the details of ONE NAC", las restricciones COMM de los grupos A/B/C aplican solo a ONE. La oferta MSC paralela puede conservar una fila general con el universo de rutas compartido, sin copiar la mercancía de ONE y respetando sus exclusiones explícitas.
                - Un recargo como Tianjin (+ arb USD100) requiere una fila compacta separada para Tianjin con originCharges=100; no se suma silenciosamente al oceanFreight. Agrupa en otra fila los POL sin arbitrario.
                - Suma únicamente recargos expresados por contenedor en surcharges. Ejemplo: ISPS USD15/cntr + P/S USD50/cntr = 65. Un cargo USD75/BL se conserva en remarks y no se convierte en costo por contenedor.
                - Recargos condicionales por peso/equipo, por ejemplo "ONE overweight surcharge: 18-21 tons - USD 200/20'", se conservan en remarks y no se suman automáticamente a surcharges.
                - Si después de las tablas aparece "General Cargo", úsalo como commodity para las filas de esas tablas salvo que una fila indique otra mercancía explícita. Conserva notas como "Subject to DTHC and local charges at both ends" en remarks.
                - Para cualquier importe desconocido usa null. Nunca uses números gigantes, infinitos, exponentes extremos ni valores centinela; surcharges debe ser un único número decimal razonable.
                - Las restricciones de espacio como "except TIANJIN/XIAMEN" se conservan en spaceComment y se aplican únicamente a la oferta o sección correspondiente.
                - Comentarios generales de disponibilidad como "space is tight", rollovers o "space availability needs to be confirmed case-by-case" se conservan en spaceComment para las filas afectadas.
                - Proyecciones futuras como "expect a further increase of approximately USD 1,000" se conservan en remarks como nota comercial; nunca las sumes al oceanFreight, surcharges ni totales actuales.
                - Usa exactamente estos nombres en cada fila: pol, poe, pod, containerType, carrier, agent, commodity, currency, freeDays, transitDays, validFrom, validTo, oceanFreight, originCharges, destinationCharges, surcharges, totalCost, totalSale, profit, margin, spaceComment y remarks. No traduzcas ni cambies los nombres.
                - pol es el puerto de origen o Port of Loading.
                - poe es el puerto marítimo de destino/entrada. Cualquier etiqueta Destination, Destination Port, Puerto destino, Port of Discharge, Arrival Port, Gateway o POE se guarda en poe. En tablas de flete marítimo, el encabezado POD normalmente significa Port of Discharge y también se guarda en poe.
                - pod es otro destino: solo se llena cuando la fuente expresa Place of Delivery, Delivery Place o Final Destination, no por el acrónimo POD aislado en una tabla marítima.
                - Nunca copies poe en pod ni deduzcas pod a partir de poe. Si no existe un lugar final de entrega explícito, devuelve pod=null.
                - Normaliza contenedores únicamente cuando sea claro: 20GP, 40GP, 40HC o 45HC. Para el patrón contractual narrativo MSC/ONE NAC con una tarifa pareada USDx/y y equipo omitido, containerType es obligatorio y debe ser 40HC; nunca lo devuelvas como null.
                - Usa moneda ISO y fechas YYYY-MM-DD. Resuelve rangos sin año, como 8-14/Aug, con el año de processingDateUtc salvo evidencia de que la vigencia cruza de diciembre a enero. Si no hay evidencia de otra divisa, currency debe ser "USD".
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
            MaximumOutputTokens: 6_000,
            TimeoutSeconds: 1_800,
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
