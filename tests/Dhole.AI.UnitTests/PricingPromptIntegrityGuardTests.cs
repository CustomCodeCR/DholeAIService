using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Application.Services;
using Dhole.AI.Domain.PromptTemplates.Entities;

namespace Dhole.AI.UnitTests;

[TestClass]
public sealed class PricingPromptIntegrityGuardTests
{
    [TestMethod]
    public void PricingEmailTemplate_AppendsMandatoryMatrixIntegrityRules()
    {
        var template = AiPromptTemplate.Create(
            "pricing-email-analysis",
            "Pricing email",
            null,
            "Extrae tarifas sin inventar valores.",
            null,
            null,
            null
        );
        var compiler = new AiPromptCompiler();

        var result = compiler.Compile(
            template,
            Array.Empty<AiProviderMessage>(),
            null
        );

        Assert.IsTrue(result.IsSuccess);
        var systemPrompt = result.Value.Messages.Single(message => message.Role == "system").Content;

        Assert.Contains("Trata cada tabla como un esquema posicional", systemPrompt);
        Assert.Contains("Nunca copies el monto de 20' a 40DV o 40HC", systemPrompt);
        Assert.Contains("cuatro filas comerciales", systemPrompt);
        Assert.Contains("doce filas por equipo", systemPrompt);
        Assert.Contains("cada oceanFreight debe poder señalar una celda monetaria concreta", systemPrompt);
        Assert.Contains("nunca restes ni sumes días por UTC", systemPrompt);
        Assert.Contains("solo la sección tarifaria más reciente", systemPrompt);
    }

    [TestMethod]
    public void NonPricingTemplate_DoesNotReceivePricingSpecificGuard()
    {
        var template = AiPromptTemplate.Create(
            "generic-assistant",
            "Generic",
            null,
            "Ayuda al usuario.",
            null,
            null,
            null
        );
        var compiler = new AiPromptCompiler();

        var result = compiler.Compile(
            template,
            Array.Empty<AiProviderMessage>(),
            null
        );

        Assert.IsTrue(result.IsSuccess);
        var systemPrompt = result.Value.Messages.Single(message => message.Role == "system").Content;
        Assert.DoesNotContain("esquema posicional", systemPrompt);
    }
}
