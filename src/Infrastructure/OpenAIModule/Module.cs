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
    extension(IHostApplicationBuilder builder)
    {
        /// <summary>
        /// Registers OpenAI as the chat client and embedding generator.
        /// </summary>
        public void AddOpenAiModule()
        {
            var llmOptions = builder.Configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();
            var ragOptions = builder.Configuration.GetSection(RagOptions.SectionName).Get<RagOptions>() ?? new RagOptions();
            var embeddingModelId = llmOptions.EmbeddingModelId ?? llmOptions.ModelId;
            var useFunctionInvocation = ragOptions.Mode is RagMode.Auto or RagMode.ToolsOnly;
        
            var uri = llmOptions.Endpoint.EndsWith("/v1") ? llmOptions.Endpoint : $"{llmOptions.Endpoint}/v1";

            var openaiClient = new OpenAIClient(
                new ApiKeyCredential(llmOptions.ApiKey
                                     ?? throw new InvalidOperationException("LLM:ApiKey is required for OpenAI provider.")),options: new OpenAIClientOptions
                {
                    Endpoint = new Uri(uri)
                });

            var chatBuilder = builder.Services.AddChatClient(
                openaiClient.GetChatClient(llmOptions.ModelId).AsIChatClient());
            if (useFunctionInvocation) chatBuilder.UseFunctionInvocation();

            builder.Services.AddEmbeddingGenerator(
                openaiClient.GetEmbeddingClient(embeddingModelId).AsIEmbeddingGenerator());
        }

        /// <summary>
        /// Registers OpenAI as the embedding generator only (used as a fallback for providers
        /// that don't support embeddings, such as Anthropic or Gemini).
        /// Reads from LLM:EmbeddingApiKey / LLM:EmbeddingModelId.
        /// </summary>
        public void AddOpenAiEmbeddingModule()
        {
            var llmOptions = builder.Configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();
            var embeddingModelId = llmOptions.EmbeddingModelId ?? llmOptions.ModelId;
            var embApiKey = llmOptions.EmbeddingApiKey ?? llmOptions.ApiKey;
            
            OpenAIClient? embOpenAi;

            if (!string.IsNullOrWhiteSpace(llmOptions.Endpoint.Trim()))
            {
                var uri = llmOptions.Endpoint.EndsWith("/v1") ? llmOptions.Endpoint : $"{llmOptions.Endpoint}/v1";
                embOpenAi = new OpenAIClient(
                    new ApiKeyCredential(embApiKey
                                         ?? throw new InvalidOperationException(
                                             "LLM:EmbeddingApiKey (or LLM:ApiKey) is required for OpenAI embedding provider. Use a dummy value for a local provider.")), options: new OpenAIClientOptions
                    {
                        Endpoint = new Uri(uri)
                    });
            }
            else
            {
                embOpenAi = new OpenAIClient(
                    new ApiKeyCredential(embApiKey
                                         ?? throw new InvalidOperationException(
                                             "LLM:EmbeddingApiKey (or LLM:ApiKey) is required for OpenAI embedding provider. Use a dummy value for a local provider.")));
            }

            builder.Services.AddEmbeddingGenerator(
                embOpenAi.GetEmbeddingClient(embeddingModelId).AsIEmbeddingGenerator());
        }
    }

    public static IHost UseOpenAiModule(this IHost host) => host;
}
