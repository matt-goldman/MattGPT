using MattGPT.Contracts;
using MattGPT.Contracts.Services;
using MattGPT.WeaviateModule.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MattGPT.WeaviateModule;

public static class Module
{
    public static IHostApplicationBuilder AddWeaviateModule(this IHostApplicationBuilder builder)
    {
        var options = builder.Configuration
            .GetSection(VectorStoreOptions.SectionName)
            .Get<VectorStoreOptions>() ?? new VectorStoreOptions();

        var endpoint = options.Endpoint
            ?? throw new InvalidOperationException("VectorStore:Endpoint is required for Weaviate provider.");

        builder.Services.Configure<VectorStoreOptions>(
            builder.Configuration.GetSection(VectorStoreOptions.SectionName));

        builder.Services.AddHttpClient<WeaviateVectorStore>(client =>
        {
            client.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
            if (!string.IsNullOrEmpty(options.ApiKey))
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {options.ApiKey}");
        });

        builder.Services.AddSingleton<IVectorStore>(sp => sp.GetRequiredService<WeaviateVectorStore>());

        return builder;
    }

    // TODO:    No-op for this module but retained for the pattern.
    public static IHost UseWeaviateModule(this IHost host) => host;
}
