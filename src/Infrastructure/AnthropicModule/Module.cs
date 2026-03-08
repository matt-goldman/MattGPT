using MattGPT.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MattGPT.AnthropicModule;

public static class Module
{
    /// <summary>
    /// Registers Anthropic as the chat client.
    /// Anthropic does not provide an embedding API; configure LLM:EmbeddingProvider
    /// to specify a separate provider (e.g. OpenAI or AzureOpenAI) for embeddings.
    /// </summary>
    public static IHostApplicationBuilder AddAnthropicModule(this IHostApplicationBuilder builder)
    {
        var llmOptions = builder.Configuration.GetSection(LlmOptions.SectionName).Get<LlmOptions>() ?? new LlmOptions();
        var ragOptions = builder.Configuration.GetSection(RagOptions.SectionName).Get<RagOptions>() ?? new RagOptions();
        var useFunctionInvocation = ragOptions.Mode is RagMode.Auto or RagMode.ToolsOnly;

        var anthropicClient = new Anthropic.SDK.AnthropicClient(llmOptions.ApiKey
            ?? throw new InvalidOperationException("LLM:ApiKey is required for Anthropic provider."));

        var chatBuilder = builder.Services.AddChatClient(anthropicClient.Messages);
        if (useFunctionInvocation) chatBuilder.UseFunctionInvocation();

        return builder;
    }

    public static IHost UseAnthropicModule(this IHost host) => host;
}
