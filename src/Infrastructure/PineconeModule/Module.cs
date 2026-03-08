using MattGPT.Contracts;
using MattGPT.Contracts.Services;
using MattGPT.PineconeModule.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pinecone;

namespace MattGPT.PineconeModule;

public static class Module
{
    public static IHostApplicationBuilder AddPineconeModule(this IHostApplicationBuilder builder)
    {
        var options = builder.Configuration
            .GetSection(VectorStoreOptions.SectionName)
            .Get<VectorStoreOptions>() ?? new VectorStoreOptions();

        var client = new PineconeClient(options.ApiKey
            ?? throw new InvalidOperationException("VectorStore:ApiKey is required for Pinecone provider."));

        builder.Services.AddSingleton(client);
        builder.Services.AddSingleton<IVectorStore>(sp =>
            new PineconeVectorStore(
                client,
                sp.GetRequiredService<ILogger<PineconeVectorStore>>(),
                options.IndexName));

        return builder;
    }

    public static IHost UsePineconeModule(this IHost host) => host;
}
