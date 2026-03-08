using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using MattGPT.AzureModule.Services;
using MattGPT.Contracts;
using MattGPT.Contracts.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;
using System.ClientModel;

namespace MattGPT.AzureModule;

public static class Module
{
    /// <summary>
    /// Registers Azure OpenAI as the chat client and embedding generator.
    /// </summary>
    public static IHostApplicationBuilder AddAzureOpenAIModule(this IHostApplicationBuilder builder)
    {
        var llmOptions = builder.Configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();
        var ragOptions = builder.Configuration.GetSection(RagOptions.SectionName).Get<RagOptions>() ?? new RagOptions();
        var embeddingModelId = llmOptions.EmbeddingModelId ?? llmOptions.ModelId;
        var useFunctionInvocation = ragOptions.Mode is RagMode.Auto or RagMode.ToolsOnly;

        var azureClient = new AzureOpenAIClient(
            new Uri(llmOptions.Endpoint),
            new ApiKeyCredential(llmOptions.ApiKey
                ?? throw new InvalidOperationException("LLM:ApiKey is required for AzureOpenAI provider.")));

        var chatBuilder = builder.Services.AddChatClient(
            azureClient.GetChatClient(llmOptions.ModelId).AsIChatClient());
        if (useFunctionInvocation) chatBuilder.UseFunctionInvocation();

        builder.Services.AddEmbeddingGenerator(
            azureClient.GetEmbeddingClient(embeddingModelId).AsIEmbeddingGenerator());

        return builder;
    }

    /// <summary>
    /// Registers Azure OpenAI as the embedding generator only (used as a fallback for
    /// providers that don't support embeddings, such as Anthropic or Gemini).
    /// Reads from LLM:EmbeddingApiKey / LLM:EmbeddingEndpoint / LLM:EmbeddingModelId.
    /// </summary>
    public static IHostApplicationBuilder AddAzureOpenAIEmbeddingModule(this IHostApplicationBuilder builder)
    {
        var llmOptions = builder.Configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();
        var embeddingModelId = llmOptions.EmbeddingModelId ?? llmOptions.ModelId;
        var embApiKey = llmOptions.EmbeddingApiKey ?? llmOptions.ApiKey;
        var embEndpoint = llmOptions.EmbeddingEndpoint ?? llmOptions.Endpoint;

        var azureClient = new AzureOpenAIClient(
            new Uri(embEndpoint),
            new ApiKeyCredential(embApiKey
                ?? throw new InvalidOperationException("LLM:EmbeddingApiKey (or LLM:ApiKey) is required for AzureOpenAI embedding provider.")));

        builder.Services.AddEmbeddingGenerator(
            azureClient.GetEmbeddingClient(embeddingModelId).AsIEmbeddingGenerator());

        return builder;
    }

    /// <summary>
    /// Registers Foundry Local (OpenAI-compatible local endpoint) as the chat client and
    /// embedding generator.
    /// </summary>
    public static IHostApplicationBuilder AddFoundryLocalModule(this IHostApplicationBuilder builder)
    {
        var llmOptions = builder.Configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();
        var ragOptions = builder.Configuration.GetSection(RagOptions.SectionName).Get<RagOptions>() ?? new RagOptions();
        var embeddingModelId = llmOptions.EmbeddingModelId ?? llmOptions.ModelId;
        var useFunctionInvocation = ragOptions.Mode is RagMode.Auto or RagMode.ToolsOnly;

        // FoundryLocal uses an OpenAI-compatible API. Local servers do not validate the API key.
        var foundryClient = new OpenAIClient(
            new ApiKeyCredential(llmOptions.ApiKey ?? "local"),
            new OpenAIClientOptions { Endpoint = new Uri(llmOptions.Endpoint) });

        var chatBuilder = builder.Services.AddChatClient(
            foundryClient.GetChatClient(llmOptions.ModelId).AsIChatClient());
        if (useFunctionInvocation) chatBuilder.UseFunctionInvocation();

        builder.Services.AddEmbeddingGenerator(
            foundryClient.GetEmbeddingClient(embeddingModelId).AsIEmbeddingGenerator());

        return builder;
    }

    /// <summary>
    /// Registers Azure AI Search as the vector store.
    /// Reads VectorStore:Endpoint, VectorStore:ApiKey, and VectorStore:IndexName from config.
    /// </summary>
    public static IHostApplicationBuilder AddAzureAISearchModule(this IHostApplicationBuilder builder)
    {
        var vsOptions = builder.Configuration
            .GetSection(VectorStoreOptions.SectionName)
            .Get<VectorStoreOptions>() ?? new VectorStoreOptions();

        var searchEndpoint = new Uri(vsOptions.Endpoint
            ?? throw new InvalidOperationException("VectorStore:Endpoint is required for AzureAISearch provider."));
        var searchCredential = new AzureKeyCredential(vsOptions.ApiKey
            ?? throw new InvalidOperationException("VectorStore:ApiKey is required for AzureAISearch provider."));

        builder.Services.AddSingleton(new SearchClient(searchEndpoint, vsOptions.IndexName, searchCredential));
        builder.Services.AddSingleton(new SearchIndexClient(searchEndpoint, searchCredential));
        builder.Services.AddSingleton<IVectorStore, AzureAISearchVectorStore>();

        return builder;
    }

    public static IHost UseAzureModule(this IHost host) => host;
}
