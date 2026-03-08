using MattGPT.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;
using System.ClientModel;

namespace MattGPT.OpenAIModule;

public static class Module
{
    /// <summary>
    /// Registers OpenAI as the chat client and embedding generator.
    /// </summary>
    public static IHostApplicationBuilder AddOpenAIModule(this IHostApplicationBuilder builder)
    {
        var llmOptions = builder.Configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();
        var ragOptions = builder.Configuration.GetSection(RagOptions.SectionName).Get<RagOptions>() ?? new RagOptions();
        var embeddingModelId = llmOptions.EmbeddingModelId ?? llmOptions.ModelId;
        var useFunctionInvocation = ragOptions.Mode is RagMode.Auto or RagMode.ToolsOnly;

        var openaiClient = new OpenAIClient(
            new ApiKeyCredential(llmOptions.ApiKey
                ?? throw new InvalidOperationException("LLM:ApiKey is required for OpenAI provider.")));

        var chatBuilder = builder.Services.AddChatClient(
            openaiClient.GetChatClient(llmOptions.ModelId).AsIChatClient());
        if (useFunctionInvocation) chatBuilder.UseFunctionInvocation();

        builder.Services.AddEmbeddingGenerator(
            openaiClient.GetEmbeddingClient(embeddingModelId).AsIEmbeddingGenerator());

        return builder;
    }

    /// <summary>
    /// Registers OpenAI as the embedding generator only (used as a fallback for providers
    /// that don't support embeddings, such as Anthropic or Gemini).
    /// Reads from LLM:EmbeddingApiKey / LLM:EmbeddingModelId.
    /// </summary>
    public static IHostApplicationBuilder AddOpenAIEmbeddingModule(this IHostApplicationBuilder builder)
    {
        var llmOptions = builder.Configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();
        var embeddingModelId = llmOptions.EmbeddingModelId ?? llmOptions.ModelId;
        var embApiKey = llmOptions.EmbeddingApiKey ?? llmOptions.ApiKey;

        var embOpenAI = new OpenAIClient(
            new ApiKeyCredential(embApiKey
                ?? throw new InvalidOperationException("LLM:EmbeddingApiKey (or LLM:ApiKey) is required for OpenAI embedding provider.")));

        builder.Services.AddEmbeddingGenerator(
            embOpenAI.GetEmbeddingClient(embeddingModelId).AsIEmbeddingGenerator());

        return builder;
    }

    public static IHost UseOpenAIModule(this IHost host) => host;
}
