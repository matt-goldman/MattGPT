using MattGPT.Contracts;
using MattGPT.Contracts.Services;
using MattGPT.PostgresModule.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MattGPT.PostgresModule;

public static class Module
{
    /// <summary>
    /// Registers the Npgsql data source for "mattgptdb" and all five PostgreSQL-backed
    /// document-store repository implementations.
    /// </summary>
    public static IHostApplicationBuilder AddPostgresDocumentModule(this IHostApplicationBuilder builder)
    {
        builder.AddNpgsqlDataSource("mattgptdb");
        builder.Services.AddSingleton<IConversationRepository, PostgresConversationRepository>();
        builder.Services.AddSingleton<IProjectNameRepository, PostgresProjectNameRepository>();
        builder.Services.AddSingleton<IUserProfileRepository, PostgresUserProfileRepository>();
        builder.Services.AddSingleton<ISystemConfigRepository, PostgresSystemConfigRepository>();
        builder.Services.AddSingleton<IChatSessionRepository, PostgresChatSessionRepository>();
        return builder;
    }

    /// <summary>
    /// Registers a Npgsql data source for <paramref name="connectionName"/> and the
    /// pgvector-backed <see cref="IVectorStore"/> implementation.
    /// When Postgres serves both roles, pass "mattgptdb" so the data source is shared.
    /// </summary>
    public static IHostApplicationBuilder AddPostgresVectorModule(
        this IHostApplicationBuilder builder, string connectionName = "mattgptdb")
    {
        builder.AddNpgsqlDataSource(connectionName);
        builder.Services.AddSingleton<IVectorStore, PostgresVectorStore>();
        return builder;
    }

    public static IHost UsePostgresModule(this IHost host) => host;
}
