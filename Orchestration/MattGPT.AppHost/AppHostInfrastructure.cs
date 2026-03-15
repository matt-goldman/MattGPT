namespace MattGPT.AppHost;

/// <summary>Resources created by <see cref="AppHostInfrastructure.AddInfrastructure"/>.</summary>
internal record InfraResources(
    IResourceBuilder<IResourceWithConnectionString>? PostgresDb,
    IResourceBuilder<IResourceWithConnectionString>? MongoDB,
    IResourceBuilder<KeycloakResource>? Keycloak);

/// <summary>
/// Provisions infrastructure resources (databases, identity provider) based on configuration.
/// </summary>
internal static class AppHostInfrastructure
{
    internal static InfraResources AddInfrastructure(this IDistributedApplicationBuilder builder)
    {
        var documentDbProvider = builder.Configuration["DocumentDb:Provider"] ?? "MongoDB";
        var vectorStoreProvider = builder.Configuration["VectorStore:Provider"] ?? "Qdrant";

        // --- Postgres (when used for document DB, vector store, or both) ---
        var isPostgresDocumentDb = documentDbProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase);
        var isPostgresVectorStore = vectorStoreProvider.Equals("Postgres", StringComparison.OrdinalIgnoreCase);

        IResourceBuilder<IResourceWithConnectionString>? postgresDb = null;
        if (isPostgresDocumentDb || isPostgresVectorStore)
        {
            // Shared database name when Postgres is the document DB; distinct name when
            // Postgres is the vector store only to avoid conflicting with a MongoDB "mattgptdb".
            var pgDbName = isPostgresDocumentDb ? "mattgptdb" : "mattgpt_vectorstore";

            postgresDb = builder.AddPostgres("postgres")
                .WithDataVolume()
                .AddDatabase(pgDbName);
        }

        // --- MongoDB (default document DB when Postgres is not selected) ---
        IResourceBuilder<IResourceWithConnectionString>? mongodb = null;
        if (!isPostgresDocumentDb)
        {
            mongodb = builder.AddMongoDB("mongodb")
                .WithDataVolume()
                .AddDatabase("mattgptdb");
        }

        // --- Keycloak (when auth is enabled and provider is Keycloak) ---
        IResourceBuilder<KeycloakResource>? keycloak = null;
        var authEnabled = builder.Configuration["Auth:Enabled"] ?? "false";
        var authProvider = builder.Configuration["Auth:Provider"] ?? "Keycloak";
        var isAuthEnabled = bool.TryParse(authEnabled, out var authEnabledBool) && authEnabledBool;

        if (isAuthEnabled && authProvider.Equals("Keycloak", StringComparison.OrdinalIgnoreCase))
        {
            keycloak = builder.AddKeycloak("keycloak")
                .WithEnvironment("KC_PROXY_HEADERS", "xforwarded")
                .WithDataVolume()
                .WithRealmImport(Path.Combine(AppContext.BaseDirectory, "keycloak", "mattgpt-realm.json"));
        }

        return new InfraResources(postgresDb, mongodb, keycloak);
    }
}
