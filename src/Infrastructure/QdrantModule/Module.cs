using MattGPT.Contracts.Services;
using MattGPT.QdrantModule.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MattGPT.QdrantModule;

public static class Module
{
    public static IHostApplicationBuilder AddQdrantModule(this IHostApplicationBuilder builder)
    {
        builder.AddQdrantClient("qdrant");
        builder.Services.AddSingleton<IVectorStore, QdrantVectorStore>();
        return builder;
    }

    // TODO:    No-op for this module but retained for the pattern.
    //          Provide warning here if possible. Not urgent.
    public static IHost UseQdrantModule(this IHost host)
    {
        return host;
    }
}
