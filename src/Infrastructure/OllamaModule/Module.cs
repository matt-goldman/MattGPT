using CommunityToolkit.Aspire.OllamaSharp;
using MattGPT.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MattGPT.OllamaModule;

public static class Module
{
    /// <summary>
    /// Registers Ollama as the chat client and embedding generator.
    /// Uses Aspire-named connections (LLM:ChatConnectionName / LLM:EmbeddingConnectionName)
    /// when configured, otherwise falls back to LLM:Endpoint.
    /// </summary>
    public static IHostApplicationBuilder AddOllamaModule(this IHostApplicationBuilder builder)
    {
        var llmOptions = builder.Configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();
        var ragOptions = builder.Configuration.GetSection(RagOptions.SectionName).Get<RagOptions>() ?? new RagOptions();
        var embeddingModelId = llmOptions.EmbeddingModelId ?? llmOptions.ModelId;
        var useFunctionInvocation = ragOptions.Mode is RagMode.Auto or RagMode.ToolsOnly;

        // Ollama models running on CPU can take a long time to load and generate,
        // especially with large RAG prompts.
        builder.Services.ConfigureHttpClientDefaults(http =>
            http.ConfigureHttpClient(client => client.Timeout = TimeSpan.FromMinutes(10)));

        if (llmOptions.ChatConnectionName is { } chatConnectionName)
        {
            var chatBuilder = builder.AddOllamaApiClient(chatConnectionName).AddChatClient();
            if (useFunctionInvocation) chatBuilder.UseFunctionInvocation();
        }
        else
        {
            var chatEndpoint = new Uri(llmOptions.Endpoint);
            var chatBuilder = builder.Services.AddChatClient(
                new OllamaSharp.OllamaApiClient(chatEndpoint, llmOptions.ModelId));
            if (useFunctionInvocation) chatBuilder.UseFunctionInvocation();
        }

        if (llmOptions.EmbeddingConnectionName is { } embeddingConnectionName)
            builder.AddOllamaApiClient(embeddingConnectionName).AddEmbeddingGenerator();
        else
        {
            var embeddingEndpoint = new Uri(llmOptions.Endpoint);
            builder.Services.AddEmbeddingGenerator(
                new OllamaSharp.OllamaApiClient(embeddingEndpoint, embeddingModelId));
        }

        return builder;
    }

    /// <summary>
    /// Registers Ollama as the embedding generator only (used as a fallback for providers
    /// that don't support embeddings). Reads from LLM:EmbeddingEndpoint / LLM:EmbeddingModelId.
    /// </summary>
    public static IHostApplicationBuilder AddOllamaEmbeddingModule(this IHostApplicationBuilder builder)
    {
        var llmOptions = builder.Configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();
        var embeddingModelId = llmOptions.EmbeddingModelId ?? llmOptions.ModelId;
        var embEndpoint = llmOptions.EmbeddingEndpoint ?? llmOptions.Endpoint;

        builder.Services.AddEmbeddingGenerator(
            new OllamaSharp.OllamaApiClient(new Uri(embEndpoint), embeddingModelId));

        return builder;
    }

    public static IHost UseOllamaModule(this IHost host) => host;
}
