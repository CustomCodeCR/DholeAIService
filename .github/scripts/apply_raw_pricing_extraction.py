from pathlib import Path
import re


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected 1 occurrence, found {count}")
    return text.replace(old, new, 1)


# The AI worker publishes extracted facts as returned by the model. DataExtraction
# performs all canonicalization and final business validation afterwards.
worker = Path("src/Dhole.AI.Workers/Workers/AiEmailAnalysisWorker.cs")
text = worker.read_text()
old = """            var parsed = PricingEmailAiExecutionFactory.NormalizeForSource(
                PricingEmailAiExecutionFactory.Merge(parsedStages),
                payload
            );"""
new = """            // AI owns semantic extraction only. DataExtraction receives these facts and
            // performs catalog resolution, canonicalization and business validation.
            var parsed = PricingEmailAiExecutionFactory.Merge(parsedStages);"""
text = replace_once(text, old, new, "AiEmailAnalysisWorker normalization handoff")
worker.write_text(text)

factory = Path("src/Dhole.AI.Workers/EmailAnalysis/PricingEmailAiExecutionFactory.cs")
text = factory.read_text()
text = replace_once(
    text,
    'taskVersion = "fcl-email-v11-body-multi-table"',
    'taskVersion = "fcl-email-v12-semantic-extraction"',
    "task version",
)
text = replace_once(
    text,
    '                    "Devuelve solo el JSON del esquema; no inventes valores.",',
    '                    "Devuelve solo el JSON del esquema; no inventes valores.",\n                    "Tu responsabilidad termina en extracción semántica. Conserva los valores observados en la fuente; DataExtraction normaliza catálogos, equipos, rutas, moneda, fechas y reglas de negocio antes de Pricing.",',
    "semantic ownership rule",
)
text = replace_once(
    text,
    '                    "Usa exactamente nombres canónicos inequívocos de catalogHints.",',
    '                    "No resuelvas IDs, codes, slugs ni nombres canónicos internos de Dhole. Devuelve el nombre o etiqueta que realmente aparece en la evidencia; DataExtraction hará la equivalencia contra Config.",',
    "catalog ownership rule",
)
text = replace_once(
    text,
    '                    "Para agent revisa primero subject y luego emailContext; usa solo una coincidencia inequívoca de catalogHints y tolera una errata mínima.",',
    '                    "Para agent revisa primero subject y luego emailContext; devuelve únicamente el nombre explícito que aparezca en la evidencia. No lo conviertas a códigos, IDs o nombres internos.",',
    "agent extraction rule",
)

# Do not serialize catalog hints into the model prompt. The transport field remains
# for backward compatibility, but the extraction profile is deliberately source-only.
pattern = re.compile(
    r"\n\s*catalogHints = payload\.CatalogHints\.Select\(group => new\s*\{\s*"
    r"group = group\.GroupSlug,\s*items = group\.Items\s*"
    r"\.Take\(MaximumCatalogItemsPerGroup\)\s*\.Select\(item => new\s*\{\s*"
    r"item\.Name,\s*item\.Code,\s*\}\),\s*\}\),",
    re.MULTILINE,
)
text, count = pattern.subn(
    '\n                normalizationOwner = "DataExtraction",',
    text,
    count=1,
)
if count != 1:
    raise SystemExit("catalogHints prompt block not found")
factory.write_text(text)

initializer = Path("src/Dhole.AI.Persistence/Initialization/AiDefaultProfilesInitializer.cs")
text = initializer.read_text()
text = replace_once(
    text,
    '            Description: "Fallback estructurado para extraer tarifas FCL desde correos y sus adjuntos cuando DataExtraction no puede analizarlos.",',
    '            Description: "Extractor semántico principal de tarifas FCL desde correos y adjuntos; DataExtraction normaliza y valida el resultado antes de Pricing.",',
    "profile description",
)
text = replace_once(
    text,
    '                Eres un extractor de tarifas FCL. Recibirás JSON con metadatos del correo, una tabla convertida a texto o el contenido textual de un adjunto y el resultado previo de DataExtraction.',
    '                Eres un extractor semántico de tarifas FCL. Recibirás metadatos del correo y evidencia textual o visual preparada por DataExtraction. Tu trabajo es identificar fielmente los hechos comerciales; DataExtraction hará después toda normalización contra Config y validación antes de Pricing.',
    "system prompt opening",
)
text = replace_once(
    text,
    '                - Usa previousExtraction como borrador, pero vuelve a comprobar cada dato contra sourceContent o la imagen adjunta. Corrige columnas, filas combinadas, fechas, montos y equipos antes de responder.',
    '                - Extrae desde sourceContent o la imagen adjunta. No dependas de un borrador determinístico para completar datos: la evidencia original manda.',
    "previous extraction rule",
)
text = replace_once(
    text,
    '                - catalogHints contiene nombres reales de Config. Cuando exista una coincidencia inequívoca, devuelve exactamente el name canónico de Config; nunca uses recuerdos externos ni inventes un catálogo.',
    '                - No resuelvas IDs, codes, slugs ni nombres canónicos internos de Dhole. Conserva el nombre o etiqueta observado en la evidencia; DataExtraction resolverá la equivalencia contra Config.',
    "profile catalog rule",
)
text = replace_once(
    text,
    '                - Para agent busca primero en el asunto y después en el cuerpo del correo. Solo usa un agente presente en catalogHints y únicamente cuando la coincidencia sea inequívoca. Tolera prefijos RE:/RV:/FWD:, separadores como // y una diferencia ortográfica mínima, por ejemplo CASTRO FALLS puede coincidir con Castro Fallas.',
    '                - Para agent busca primero en el asunto y después en el cuerpo del correo. Devuelve solo el nombre explícito observado; DataExtraction resolverá errores ortográficos y equivalencias de catálogo.',
    "profile agent rule",
)
text = replace_once(
    text,
    '                - Los grupos de Config son siempre: carriers, pol, pod, poe, currencies, agents, container-types y pricing-imports-profiles.',
    '                - No necesitas conocer los grupos internos de Config. La resolución carriers/POL/POE/POD/currencies/agents/equipment pertenece a DataExtraction.',
    "profile config groups rule",
)
text = replace_once(
    text,
    '                - Normaliza contenedores únicamente cuando sea claro: 20GP, 40GP, 40HC o 45HC. Para el patrón contractual narrativo MSC/ONE NAC con una tarifa pareada USDx/y y equipo omitido, containerType es obligatorio y debe ser 40HC; nunca lo devuelvas como null.',
    '                - Para containerType conserva la etiqueta de equipo observada cuando exista (por ejemplo 20-DV, 40-DV, 40-HC). Solo infiere un equipo cuando la propia evidencia lo determine inequívocamente; DataExtraction hará la normalización final de tamaño y tipo.',
    "profile equipment rule",
)
text = replace_once(
    text,
    '                - Usa moneda ISO y fechas YYYY-MM-DD. Resuelve rangos sin año, como 8-14/Aug, con el año de processingDateUtc salvo evidencia de que la vigencia cruza de diciembre a enero. Si no hay evidencia de otra divisa, currency debe ser "USD".',
    '                - Devuelve currency y fechas en el formato del esquema cuando sean inequívocos. Puedes usar processingDateUtc únicamente para resolver el año ausente; DataExtraction volverá a normalizar moneda y fechas antes de persistir.',
    "profile date/currency rule",
)

# Prefer Qwen3 14B for structured extraction when that model is registered,
# active and available. Existing ranking remains the fallback when it is absent.
needle = '''        if (
            identity.Contains("llama3.2", StringComparison.Ordinal)
            || identity.Contains("llama3.1", StringComparison.Ordinal)
        )'''
insert = '''        if (
            identity.Contains("qwen3", StringComparison.Ordinal)
            && identity.Contains("14b", StringComparison.Ordinal)
        )
        {
            return -2;
        }

        if (identity.Contains("qwen3", StringComparison.Ordinal))
        {
            return -1;
        }

        if (
            identity.Contains("llama3.2", StringComparison.Ordinal)
            || identity.Contains("llama3.1", StringComparison.Ordinal)
        )'''
text = replace_once(text, needle, insert, "Qwen ranking")
initializer.write_text(text)
