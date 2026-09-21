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
        /// Adds the seleceted AI provider to the application builder.
        /// </summary>
        /// <returns><see cref="WebApplicationBuilder"/></returns>
        public WebApplicationBuilder AddAiProvider()
        {
            // Register LLM services based on configuration.
            builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));
            var llmOptions = builder.Configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();

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
                    builder.AddOpenAIModule();
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
                    builder.AddOpenAIEmbeddingModule();
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
}