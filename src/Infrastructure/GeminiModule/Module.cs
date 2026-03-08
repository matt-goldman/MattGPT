using MattGPT.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MattGPT.GeminiModule;

public static class Module
{
    /// <summary>
    /// Registers Gemini as the chat client.
    /// Gemini embedding support through Microsoft.Extensions.AI is limited; configure
    /// LLM:EmbeddingProvider to specify a separate provider for embeddings if needed.
    /// </summary>
    public static IHostApplicationBuilder AddGeminiModule(this IHostApplicationBuilder builder)
    {
        var llmOptions = builder.Configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();
        var ragOptions = builder.Configuration.GetSection(RagOptions.SectionName).Get<RagOptions>() ?? new RagOptions();
        var useFunctionInvocation = ragOptions.Mode is RagMode.Auto or RagMode.ToolsOnly;

        var geminiOptions = new GeminiDotnet.GeminiClientOptions
        {
            ApiKey = llmOptions.ApiKey
                ?? throw new InvalidOperationException("LLM:ApiKey is required for Gemini provider."),
            ModelId = llmOptions.ModelId
        };

        var chatBuilder = builder.Services.AddChatClient(
            new GeminiDotnet.Extensions.AI.GeminiChatClient(geminiOptions));
        if (useFunctionInvocation) chatBuilder.UseFunctionInvocation();

        return builder;
    }

    public static IHost UseGeminiModule(this IHost host) => host;
}
