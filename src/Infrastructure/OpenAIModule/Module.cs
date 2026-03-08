using Azure.AI.OpenAI;
using MattGPT.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;
using System.ClientModel;

namespace MattGPT.OpenAIModule;

/// <summary>
/// Infrastructure module that registers the configured LLM chat client and embedding generator.
/// Supports all current providers: Ollama, FoundryLocal, AzureOpenAI, OpenAI, Anthropic, Gemini.
/// </summary>
public static class Module
{
    public static IHostApplicationBuilder AddOpenAIModule(this IHostApplicationBuilder builder)
    {
        var llmOptions = builder.Configuration
            .GetSection(LlmOptions.SectionName)
            .Get<LlmOptions>() ?? new LlmOptions();

        var ragOptions = builder.Configuration
            .GetSection(RagOptions.SectionName)
            .Get<RagOptions>() ?? new RagOptions();

        var embeddingModelId = llmOptions.EmbeddingModelId ?? llmOptions.ModelId;
        var useFunctionInvocation = ragOptions.Mode is RagMode.Auto or RagMode.ToolsOnly;

        switch (llmOptions.Provider.ToLowerInvariant())
        {
            case "ollama":
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
                {
                    builder.AddOllamaApiClient(embeddingConnectionName).AddEmbeddingGenerator();
                }
                else
                {
                    var embeddingEndpoint = new Uri(llmOptions.Endpoint);
                    builder.Services.AddEmbeddingGenerator(
                        new OllamaSharp.OllamaApiClient(embeddingEndpoint, embeddingModelId));
                }
                break;

            case "foundrylocal":
                // FoundryLocal uses an OpenAI-compatible API. Local servers do not validate
                // the API key, but the SDK requires a non-null value.
                var foundryClient = new OpenAIClient(
                    new ApiKeyCredential(llmOptions.ApiKey ?? "local"),
                    new OpenAIClientOptions { Endpoint = new Uri(llmOptions.Endpoint) });
                {
                    var chatBuilder = builder.Services.AddChatClient(
                        foundryClient.GetChatClient(llmOptions.ModelId).AsIChatClient());
                    if (useFunctionInvocation) chatBuilder.UseFunctionInvocation();
                }
                builder.Services.AddEmbeddingGenerator(
                    foundryClient.GetEmbeddingClient(embeddingModelId).AsIEmbeddingGenerator());
                break;

            case "azureopenai":
                var azureClient = new AzureOpenAIClient(
                    new Uri(llmOptions.Endpoint),
                    new ApiKeyCredential(llmOptions.ApiKey
                        ?? throw new InvalidOperationException("LLM:ApiKey is required for AzureOpenAI provider.")));
                {
                    var chatBuilder = builder.Services.AddChatClient(
                        azureClient.GetChatClient(llmOptions.ModelId).AsIChatClient());
                    if (useFunctionInvocation) chatBuilder.UseFunctionInvocation();
                }
                builder.Services.AddEmbeddingGenerator(
                    azureClient.GetEmbeddingClient(embeddingModelId).AsIEmbeddingGenerator());
                break;

            case "openai":
                var openaiClient = new OpenAIClient(
                    new ApiKeyCredential(llmOptions.ApiKey
                        ?? throw new InvalidOperationException("LLM:ApiKey is required for OpenAI provider.")));
                {
                    var chatBuilder = builder.Services.AddChatClient(
                        openaiClient.GetChatClient(llmOptions.ModelId).AsIChatClient());
                    if (useFunctionInvocation) chatBuilder.UseFunctionInvocation();
                }
                builder.Services.AddEmbeddingGenerator(
                    openaiClient.GetEmbeddingClient(embeddingModelId).AsIEmbeddingGenerator());
                break;

            case "anthropic":
                // Anthropic does not provide an embedding API; use LLM:EmbeddingProvider for embeddings.
                var anthropicClient = new Anthropic.SDK.AnthropicClient(llmOptions.ApiKey
                    ?? throw new InvalidOperationException("LLM:ApiKey is required for Anthropic provider."));
                {
                    var chatBuilder = builder.Services.AddChatClient(anthropicClient.Messages);
                    if (useFunctionInvocation) chatBuilder.UseFunctionInvocation();
                }
                break;

            case "gemini":
                // Gemini embedding support through M.E.AI is limited; use EmbeddingProvider fallback if needed.
                var geminiOptions = new GeminiDotnet.GeminiClientOptions
                {
                    ApiKey = llmOptions.ApiKey
                        ?? throw new InvalidOperationException("LLM:ApiKey is required for Gemini provider."),
                    ModelId = llmOptions.ModelId
                };
                {
                    var chatBuilder = builder.Services.AddChatClient(
                        new GeminiDotnet.Extensions.AI.GeminiChatClient(geminiOptions));
                    if (useFunctionInvocation) chatBuilder.UseFunctionInvocation();
                }
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported LLM provider: '{llmOptions.Provider}'. " +
                    "Supported values: Ollama, FoundryLocal, AzureOpenAI, OpenAI, Anthropic, Gemini.");
        }

        // --- Embedding provider fallback ---
        // For providers that don't support embeddings natively (Anthropic, Gemini), a separate
        // embedding provider can be configured via LLM:EmbeddingProvider.
        if (llmOptions.EmbeddingProvider is { } embProvider && !string.IsNullOrWhiteSpace(embProvider))
        {
            var embApiKey = llmOptions.EmbeddingApiKey ?? llmOptions.ApiKey;
            var embEndpoint = llmOptions.EmbeddingEndpoint ?? llmOptions.Endpoint;

            switch (embProvider.ToLowerInvariant())
            {
                case "openai":
                    var embOpenAI = new OpenAIClient(
                        new ApiKeyCredential(embApiKey
                            ?? throw new InvalidOperationException("LLM:EmbeddingApiKey (or LLM:ApiKey) is required for OpenAI embedding provider.")));
                    builder.Services.AddEmbeddingGenerator(
                        embOpenAI.GetEmbeddingClient(embeddingModelId).AsIEmbeddingGenerator());
                    break;

                case "azureopenai":
                    var embAzure = new AzureOpenAIClient(
                        new Uri(embEndpoint),
                        new ApiKeyCredential(embApiKey
                            ?? throw new InvalidOperationException("LLM:EmbeddingApiKey (or LLM:ApiKey) is required for AzureOpenAI embedding provider.")));
                    builder.Services.AddEmbeddingGenerator(
                        embAzure.GetEmbeddingClient(embeddingModelId).AsIEmbeddingGenerator());
                    break;

                case "ollama":
                    var embOllamaEndpoint = new Uri(embEndpoint);
                    builder.Services.AddEmbeddingGenerator(
                        new OllamaSharp.OllamaApiClient(embOllamaEndpoint, embeddingModelId));
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported LLM:EmbeddingProvider: '{embProvider}'. Supported values: OpenAI, AzureOpenAI, Ollama.");
            }
        }

        return builder;
    }

    // TODO:    No-op for this module but retained for the pattern.
    public static IHost UseOpenAIModule(this IHost host) => host;
}
