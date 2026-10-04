using MattGPT.AnthropicModule;
using MattGPT.AzureModule;
using MattGPT.Contracts;
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
    /// Validates the reranking-related settings across <see cref="RagOptions"/> and <see cref="LlmOptions"/>:
    /// a reranking model must be configured when <see cref="RagOptions.UseReranking"/> is enabled, and
    /// <see cref="RagOptions.MaxEmbeddingChars"/>, when set, must be positive.
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

        if (ragOptions.MaxEmbeddingChars is <= 0)
            errors.Add(
                $"{RagOptions.SectionName}:{nameof(RagOptions.MaxEmbeddingChars)} must be greater than 0 " +
                $"(was {ragOptions.MaxEmbeddingChars}). Leave it unset to use the default.");

        if (errors.Count > 0)
            throw new InvalidOperationException(
                "Invalid reranking configuration: " + string.Join(" ", errors));
    }
}