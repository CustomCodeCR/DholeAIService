using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Infrastructure.Providers.Ollama;

namespace Dhole.AI.UnitTests;

[TestClass]
public sealed class OllamaStructuredThroughputTests
{
    private static AiProviderChatRequest Request(bool structured) => new(
        [new AiProviderMessage("user", "Tarifa LCL USD 100")],
        0m,
        2048,
        structured,
        """{"type":"object","properties":{"rows":{"type":"array"}}}""");

    [TestMethod]
    public void QwenStructuredRequest_ReusesModelAndUsesFastJsonGrammar()
    {
        var payload = OllamaRequestMapper.CreateChatPayload(
            Request(true), "qwen3.5:35b-a3b-q4_K_M", false);

        Assert.AreEqual("15m", payload["keep_alive"]?.GetValue<string>());
        Assert.AreEqual("json", payload["format"]?.GetValue<string>());
        Assert.AreEqual(false, payload["think"]?.GetValue<bool>());
        Assert.IsNotNull(payload["options"]?["num_predict"]);
    }

    [TestMethod]
    public void OtherStructuredModel_DoesNotRemainResident()
    {
        var payload = OllamaRequestMapper.CreateChatPayload(
            Request(true), "some-structured-model:latest", false);

        Assert.AreEqual("0", payload["keep_alive"]?.GetValue<string>());
        Assert.IsNotNull(payload["format"]);
    }

    [TestMethod]
    public void UnstructuredChat_RemainsUnchanged()
    {
        var payload = OllamaRequestMapper.CreateChatPayload(
            Request(false), "qwen3.5:35b-a3b-q4_K_M", false);

        Assert.AreEqual("15m", payload["keep_alive"]?.GetValue<string>());
        Assert.IsNull(payload["format"]);
    }
}
