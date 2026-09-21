using System.Threading.Channels;
using MattGPT.ApiService.Services;
using MattGPT.Contracts;
using Microsoft.AspNetCore.Http.Features;

namespace MattGPT.ApiService.Extensions;

/// <summary>
/// Extension methods for configuring application services.
/// </summary>
public static class ServiceExtensions
{
    /// <param name="builder"></param>
    extension(WebApplicationBuilder builder)
    {
        /// <summary>
        /// Configures and adds application services to the service collection.
        /// </summary>
        /// <returns>The updated WebApplicationBuilder instance.</returns>
        public WebApplicationBuilder AddApplicationServices()
        {
            // Add services to the container.
            builder.Services.AddProblemDetails();
            builder.Services.AddSingleton<ConversationParser>();
            builder.Services.AddSingleton<ImportJobStore>();

            var defaultChannelOptions = new BoundedChannelOptions(50)
            {
                FullMode        = BoundedChannelFullMode.Wait,
                SingleReader    = true
            };

            builder.Services.AddSingleton(Channel.CreateBounded<ImportJobRequest>(defaultChannelOptions));
            builder.Services.AddHostedService<ImportProcessingService>();

            builder.Services.AddSingleton(Channel.CreateBounded<EmbedJobRequest>(defaultChannelOptions));
            builder.Services.AddHostedService<EmbedProcessingService>();

            builder.Services.AddScoped<SummarisationService>();

            builder.Services.AddSingleton(TimeProvider.System);

            builder.Services.AddScoped<EmbeddingService>();

            builder.AddVectorStorage();

            builder.Services.AddScoped<RagService>();
            builder.Services.Configure<RagOptions>(builder.Configuration.GetSection(RagOptions.SectionName));
            var ragOptions = builder.Configuration.GetSection(RagOptions.SectionName).Get<RagOptions>() ?? new RagOptions();

            // Register the retrieval tools when RAG mode supports tool calling (Auto or ToolsOnly).
            // search_memories searches by meaning, search_memories_keyword by literal words; the LLM
            // picks whichever fits the question.
            if (ragOptions.Mode is RagMode.Auto or RagMode.ToolsOnly)
            {
                builder.Services.AddScoped<SearchMemoriesTool>();
                builder.Services.AddScoped<KeywordSearchMemoriesTool>();
            }

            builder.Services.AddScoped<ChatSessionService>();
            builder.Services.Configure<ChatSessionOptions>(builder.Configuration.GetSection(ChatSessionOptions.SectionName));

            // Allow large multipart form uploads on this service.
            builder.Services.Configure<FormOptions>(options =>
            {
                options.MultipartBodyLengthLimit = 262_144_000; // 250 MB
            });

            return builder;
        }
    }
}