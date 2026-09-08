using System.Text.Json;
using CustomCodeFramework.Core.Results;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Application.Abstractions.Services;
using Dhole.AI.Application.Shared;
using Dhole.AI.Domain.PromptTemplates.Entities;

namespace Dhole.AI.Application.Services;

public sealed class AiPromptCompiler : IAiPromptCompiler
{
    private const string PricingEmailTemplateKey = "pricing-email-analysis";

    private const string PricingEmailIntegrityGuard = """
        REGLAS DE INTEGRIDAD OBLIGATORIAS PARA TARIFAS DE PRICING:
        - Trata cada tabla como un esquema posicional. Una vez identificados N encabezados, consume exactamente N celdas por fila lógica y conserva cada valor en la columna de esa misma fila. Nunca corras, desplaces, arrastres o reutilices una celda en otra columna o en la siguiente fila.
        - Para una matriz con encabezados POL, POD, CARRIER, 20', 40'/40HC, Free time, Effective Date y Expiry date, cada bloque de ocho valores posterior representa una fila comercial independiente. Un cambio de POL, carrier o monto inicia otra fila y jamás puede fusionarse con la anterior.
        - El monto de 20' pertenece únicamente al equipo 20DV/20GP de ESA fila. El monto de 40'/40HC pertenece únicamente a los equipos 40DV/40GP y 40HC de ESA fila. Nunca copies el monto de 20' a 40DV o 40HC y nunca copies un monto de otra fila.
        - Cuando 40'/40HC comparte una sola celda de monto, genera exactamente dos filas de equipo para esa celda: una 40DV/40GP y una 40HC, ambas con ese monto compartido. Además genera la fila 20DV/20GP usando exclusivamente la celda 20'.
        - Conserva todas las filas fuente. Por ejemplo, cuatro filas comerciales que contienen 20' y 40'/40HC producen doce filas por equipo antes de que DataExtraction expanda variantes de ruta. No reduzcas la cardinalidad a la primera fila ni a la primera naviera.
        - Antes de responder, verifica trazabilidad: cada oceanFreight debe poder señalar una celda monetaria concreta de la misma fila fuente y del encabezado de equipo correspondiente. Si no puedes demostrar esa correspondencia, devuelve warning y no inventes la asociación.
        - sourceContent es la autoridad. Si previousExtraction está incompleto, desplazado, duplica montos o contradice una celda explícita de la tabla, corrígelo usando la fuente original; nunca fuerces la fuente para que coincida con un borrador erróneo.
        - Las fechas Effective Date y Expiry date son fechas comerciales sin zona horaria. Conserva exactamente el día calendario publicado; nunca restes ni sumes días por UTC, zona horaria o conversión de timestamp.
        - En hilos de correo, solo la sección tarifaria más reciente es vigente. No mezcles una tarifa actual con montos, vigencias o carriers de una respuesta citada anterior.
        """;

    public Result<AiCompiledPrompt> Compile(
        AiPromptTemplate? template,
        IReadOnlyCollection<AiProviderMessage> messages,
        IReadOnlyCollection<AiPromptVariable>? variables
    )
    {
        var values = (variables ?? [])
            .GroupBy(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value,
                StringComparer.OrdinalIgnoreCase
            );

        var result = new List<AiProviderMessage>();

        if (template is not null)
        {
            var requiredVariables = ParseRequiredVariables(template.VariablesJson);

            var missing = requiredVariables.Where(name => !values.ContainsKey(name)).ToArray();

            if (missing.Length > 0)
            {
                return Result.Failure<AiCompiledPrompt>(AiApplicationErrors.MissingPromptVariable);
            }

            if (!string.IsNullOrWhiteSpace(template.SystemPrompt))
            {
                var systemPrompt = Render(template.SystemPrompt, values);
                if (template.Key.Equals(PricingEmailTemplateKey, StringComparison.OrdinalIgnoreCase))
                {
                    systemPrompt = $"{systemPrompt.TrimEnd()}\n\n{PricingEmailIntegrityGuard}";
                }

                result.Add(new AiProviderMessage("system", systemPrompt));
            }

            if (!string.IsNullOrWhiteSpace(template.UserPromptTemplate))
            {
                result.Add(
                    new AiProviderMessage("user", Render(template.UserPromptTemplate, values))
                );
            }
        }

        result.AddRange(
            messages.Select(message => new AiProviderMessage(
                NormalizeRole(message.Role),
                message.Content,
                message.Images
            ))
        );

        if (result.Count == 0)
        {
            return Result.Failure<AiCompiledPrompt>(AiApplicationErrors.InvalidPromptTemplate);
        }

        return Result.Success(new AiCompiledPrompt(result));
    }

    private static IReadOnlyCollection<string> ParseRequiredVariables(string? variablesJson)
    {
        if (string.IsNullOrWhiteSpace(variablesJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(variablesJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Render(string template, IReadOnlyDictionary<string, string> variables)
    {
        var result = template;

        foreach (var variable in variables)
        {
            result = result.Replace(
                $"{{{{{variable.Key}}}}}",
                variable.Value,
                StringComparison.OrdinalIgnoreCase
            );
        }

        return result;
    }

    private static string NormalizeRole(string role)
    {
        return role.Trim().ToLowerInvariant() switch
        {
            "system" => "system",
            "assistant" => "assistant",
            "tool" => "tool",
            _ => "user",
        };
    }
}
