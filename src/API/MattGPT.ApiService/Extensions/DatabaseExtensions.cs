using MattGPT.AzureModule;
using MattGPT.Contracts;
using MattGPT.MongoDBModule;
using MattGPT.PineconeModule;
using MattGPT.PostgresModule;
using MattGPT.QdrantModule;
using MattGPT.WeaviateModule;

namespace MattGPT.ApiService.Extensions;

/// <summary>
/// Extension methods for configuring document storage.
/// </summary>
public static class DatabaseExtensions
{
    private static bool _vectorStoreConfigured;
    private static bool _documentStoreConfigured;
    private static DocumentDbOptions? _documentDbOptions = null;

    /// <param name="builder"></param>
    extension(WebApplicationBuilder builder)
    {
        /// <summary>
        /// Maps the selected document DB provider.
        /// </summary>
        /// <returns><see cref="WebApplicationBuilder"/></returns>
        public WebApplicationBuilder AddDocumentStorage()
        {
            // Read document DB provider early so we can configure Identity's backing store.
            builder.Services.Configure<DocumentDbOptions>(builder.Configuration.GetSection(DocumentDbOptions.SectionName));
            _documentDbOptions = builder.Configuration.GetSection(DocumentDbOptions.SectionName).Get<DocumentDbOptions>() ?? new DocumentDbOptions();

            // Track whether the vector store has already been configured (e.g. when Postgres serves both roles).
            if (_vectorStoreConfigured) return builder;

            // Register document DB services based on provider.
            if (_documentDbOptions.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase))
            {
                builder.AddPostgresDocumentModule();

                // If the vector store is also Postgres, configure it here and set the flag.
                var vsOptions = builder.Configuration.GetSection(VectorStoreOptions.SectionName).Get<VectorStoreOptions>() ?? new VectorStoreOptions();
            
                if (!vsOptions.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase)) return builder;
            
                builder.AddPostgresVectorModule();
                _vectorStoreConfigured = true;
            }
            else
            {
                // MongoDB (default) — register MongoDB client and repository implementations.
                builder.AddMongoDBModule();
            }
        
            _documentStoreConfigured = true;
            return builder;
        }

        /// <summary>
        /// Maps the selected vector DB provider.
        /// NOTE: You MUST call <see cref="AddDocumentStorage"/> before calling this method.
        /// </summary>
        /// <throws><see cref="InvalidOperationException"/></throws>
        /// <returns><see cref="WebApplicationBuilder"/></returns>
        public WebApplicationBuilder AddVectorStorage()
        {
            if (!_documentStoreConfigured || _documentDbOptions == null) throw new InvalidOperationException("You must call AddDocumentStorage before calling AddVectorStorage.");
        
            // Skip vector store registration if it was already configured above (e.g. Postgres serving both roles).
            if (_vectorStoreConfigured) return builder;
        
            builder.Services.Configure<VectorStoreOptions>(builder.Configuration.GetSection(VectorStoreOptions.SectionName));
            var vectorStoreOptions = builder.Configuration.GetSection(VectorStoreOptions.SectionName).Get<VectorStoreOptions>() ?? new VectorStoreOptions();
        
            switch (vectorStoreOptions.Provider.ToLowerInvariant())
            {
                case "qdrant":
                    builder.AddQdrantModule();
                    break;

                case "postgres":
                {
                    // Connection "mattgptdb" is already registered when Postgres is also the doc DB provider.
                    var pgConn = _documentDbOptions.Provider.Equals("Postgres", StringComparison.OrdinalIgnoreCase)
                        ? "mattgptdb" : "mattgpt_vectorstore";
                    builder.AddPostgresVectorModule(pgConn);
                }
                    break;

                case "azureaisearch":
                    builder.AddAzureAISearchModule();
                    break;

                case "pinecone":
                    builder.AddPineconeModule();
                    break;

                case "weaviate":
                    builder.AddWeaviateModule();
                    break;

                default:
                    Console.Error.WriteLine($"[WARNING] Unknown VectorStore:Provider '{vectorStoreOptions.Provider}'; falling back to Qdrant.");
                    break;
            }

            return builder;
        }
    }
}