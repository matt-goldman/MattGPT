using MattGPT.AnthropicModule;
using MattGPT.ApiService.Services;
using MattGPT.AzureModule;
using MattGPT.Contracts;
using MattGPT.Contracts.Services;
using MattGPT.GeminiModule;
using MattGPT.OllamaModule;
using MattGPT.OpenAIModule;

namespace MattGPT.ApiService.Extensions;

/// <summary>
/// Extension methods for configuring AI services.
/// </summary>
public static class AiExtensions
{
    /// <param name="builder"></param>
    extension(WebApplicationBuilder builder)
    {
        /// <summary>
        /// Adds the selected AI provider to the application builder.
        /// </summary>
        /// <returns><see cref="WebApplicationBuilder"/></returns>
        public WebApplicationBuilder AddAiProvider()
        {
            // Register LLM services based on configuration.
            builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));
            var llmOptions = builder.Configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();

            var ragOptions = builder.Configuration.GetSection(RagOptions.SectionName).Get<RagOptions>() ?? new RagOptions();
            ValidateRerankingOptions(ragOptions, llmOptions);

            // Reranking runs inside MemoryRetriever, which uses the reranker only when one is registered.
            if (ragOptions.UseReranking)
                builder.Services.AddHttpClient<IReranker, CohereCompatibleReranker>();

            Console.WriteLine($"Starting API with LLM provider {llmOptions.Provider} and embeddings provider {llmOptions.EmbeddingProvider}");

            switch (llmOptions.Provider.ToLowerInvariant())
            {
                case "ollama":
                    builder.AddOllamaModule();
                    break;

                case "foundrylocal":
                    builder.AddFoundryLocalModule();
                    break;

                case "azureopenai":
                    builder.AddAzureOpenAIModule();
                    break;

                case "openai":
                    builder.AddOpenAiModule();
                    break;

                case "anthropic":
                    builder.AddAnthropicModule();
                    break;

                case "gemini":
                    builder.AddGeminiModule();
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported LLM provider: '{llmOptions.Provider}'. " +
                        "Supported values: Ollama, FoundryLocal, AzureOpenAI, OpenAI, Anthropic, Gemini.");
            }

            // Embedding provider fallback — for providers without native embeddings (e.g. Anthropic, Gemini).
            if (string.IsNullOrWhiteSpace(llmOptions.EmbeddingProvider)) return builder;
            switch (llmOptions.EmbeddingProvider.ToLowerInvariant())
            {
                case "openai":
                    builder.AddOpenAiEmbeddingModule();
                    break;

                case "azureopenai":
                    builder.AddAzureOpenAIEmbeddingModule();
                    break;

                case "ollama":
                    builder.AddOllamaEmbeddingModule();
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Unsupported LLM:EmbeddingProvider: '{llmOptions.EmbeddingProvider}'. Supported values: OpenAI, AzureOpenAI, Ollama.");
            }
            return builder;
        }
    }

    /// <summary>
    /// Validates the retrieval settings across <see cref="RagOptions"/> and <see cref="LlmOptions"/> —
    /// reranking, chunking and retained breadth.
    /// When <see cref="RagOptions.UseReranking"/> is enabled: a reranking model is required, the provider
    /// (if set) must be a supported format, the endpoint (if set) must be an absolute http(s) URL, and the
    /// candidate count and document length must be positive. <see cref="RagOptions.MaxEmbeddingChars"/>,
    /// when set, must be positive regardless.
    /// </summary>
    /// <exception cref="InvalidOperationException">One or more settings are invalid.</exception>
    internal static void ValidateRerankingOptions(RagOptions ragOptions, LlmOptions llmOptions)
    {
        var errors = new List<string>();

        if (ragOptions.UseReranking && string.IsNullOrWhiteSpace(llmOptions.RerankingModelId))
            errors.Add(
                $"{LlmOptions.SectionName}:{nameof(LlmOptions.RerankingModelId)} is required when " +
                $"{RagOptions.SectionName}:{nameof(RagOptions.UseReranking)} is true. Configure a reranking model, " +
                $"or set {RagOptions.SectionName}:{nameof(RagOptions.UseReranking)} to false.");

        if (ragOptions.UseReranking)
        {
            if (llmOptions.RerankingProvider is { } provider
                && !provider.Equals(CohereCompatibleReranker.ProviderName, StringComparison.OrdinalIgnoreCase))
                errors.Add(
                    $"Unsupported {LlmOptions.SectionName}:{nameof(LlmOptions.RerankingProvider)} '{provider}'. " +
                    $"Supported values: {CohereCompatibleReranker.ProviderName} (any Cohere-compatible rerank API).");

            if (llmOptions.RerankingEndpoint is { } endpoint
                && !(Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
                errors.Add(
                    $"{LlmOptions.SectionName}:{nameof(LlmOptions.RerankingEndpoint)} must be an absolute http(s) URL " +
                    $"including the rerank path (e.g. {CohereCompatibleReranker.DefaultEndpoint}); was '{endpoint}'.");

            if (ragOptions.RerankCandidateCount <= 0)
                errors.Add(
                    $"{RagOptions.SectionName}:{nameof(RagOptions.RerankCandidateCount)} must be greater than 0 " +
                    $"(was {ragOptions.RerankCandidateCount}).");

            if (ragOptions.RerankDocumentChars <= 0)
                errors.Add(
                    $"{RagOptions.SectionName}:{nameof(RagOptions.RerankDocumentChars)} must be greater than 0 " +
                    $"(was {ragOptions.RerankDocumentChars}).");
        }

        if (ragOptions.MaxEmbeddingChars is <= 0)
            errors.Add(
                $"{RagOptions.SectionName}:{nameof(RagOptions.MaxEmbeddingChars)} must be greater than 0 " +
                $"(was {ragOptions.MaxEmbeddingChars}). Leave it unset to use the default.");

        if (ragOptions.MaxChunkChars <= 0)
            errors.Add(
                $"{RagOptions.SectionName}:{nameof(RagOptions.MaxChunkChars)} must be greater than 0 " +
                $"(was {ragOptions.MaxChunkChars}).");

        if (ragOptions.ChunkCandidateMultiplier <= 0)
            errors.Add(
                $"{RagOptions.SectionName}:{nameof(RagOptions.ChunkCandidateMultiplier)} must be greater than 0 " +
                $"(was {ragOptions.ChunkCandidateMultiplier}).");

        if (ragOptions.RetainedConversations <= 0)
            errors.Add(
                $"{RagOptions.SectionName}:{nameof(RagOptions.RetainedConversations)} must be greater than 0 " +
                $"(was {ragOptions.RetainedConversations}). Retaining nothing leaves generation with no context.");

        if (ragOptions.RetainedRelativeScore is < 0f or > 1f)
            errors.Add(
                $"{RagOptions.SectionName}:{nameof(RagOptions.RetainedRelativeScore)} must be between 0 and 1 " +
                $"(was {ragOptions.RetainedRelativeScore}); it is a fraction of the best score. Use 0 to retain by count alone.");

        if (ragOptions.NeighbourWindow < 0)
            errors.Add(
                $"{RagOptions.SectionName}:{nameof(RagOptions.NeighbourWindow)} cannot be negative " +
                $"(was {ragOptions.NeighbourWindow}). Use 0 to disable neighbour expansion.");

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Invalid reranking configuration: " + string.Join(" ", errors));
    }
}