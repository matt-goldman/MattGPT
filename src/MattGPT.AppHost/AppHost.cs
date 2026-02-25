#pragma warning disable ASPIREINTERACTION001
using Microsoft.Extensions.DependencyInjection;

var builder = DistributedApplication.CreateBuilder(args);

var mongodb = builder.AddMongoDB("mongodb")
    .AddDatabase("mattgptdb");

var qdrant = builder.AddQdrant("qdrant");

// Read defaults from configuration, falling back to sensible values.
var defaultProvider = builder.Configuration["LLM:Provider"] ?? "Ollama";
var defaultModelId = builder.Configuration["LLM:ModelId"] ?? "llama3.2";
var defaultEmbeddingModelId = builder.Configuration["LLM:EmbeddingModelId"] ?? defaultModelId;

// Mutable state populated by the interaction service prompt before any resources start.
var chosenProvider = defaultProvider;
var chosenModelId = defaultModelId;
var chosenEmbeddingModelId = defaultEmbeddingModelId;
var chosenEndpoint = builder.Configuration["LLM:Endpoint"] ?? string.Empty;
var chosenApiKey = builder.Configuration["LLM:ApiKey"];

// Configure the API service; env-var callbacks read the chosen values at resource start time,
// after the interaction service prompt has been answered.
var apiService = builder.AddProject<Projects.MattGPT_ApiService>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(mongodb)
    .WaitFor(mongodb)
    .WithReference(qdrant)
    .WaitFor(qdrant)
    .WithEnvironment(ctx =>
    {
        ctx.EnvironmentVariables["LLM__Provider"] = chosenProvider;
        ctx.EnvironmentVariables["LLM__ModelId"] = chosenModelId;
        ctx.EnvironmentVariables["LLM__EmbeddingModelId"] = chosenEmbeddingModelId;
        if (!string.IsNullOrEmpty(chosenEndpoint))
            ctx.EnvironmentVariables["LLM__Endpoint"] = chosenEndpoint;
        if (!string.IsNullOrEmpty(chosenApiKey))
            ctx.EnvironmentVariables["LLM__ApiKey"] = chosenApiKey;
    });

// Ollama resources are added based on the build-time default provider.
// Aspire resources must be registered before Build() is called, so this block cannot be
// deferred until after the interaction service prompt. If the user switches to a different
// provider via the interaction, the Ollama container will still start but remain unused.
// To avoid that overhead, set LLM:Provider in appsettings.json before starting.
if (defaultProvider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
{
    var ollama = builder.AddOllama("ollama");
    var chatModel = ollama.AddModel(defaultModelId);

    apiService
        .WithReference(chatModel)
        .WaitFor(chatModel)
        .WithEnvironment(ctx =>
        {
            // Only wire the Aspire Ollama connection name when the user hasn't switched to another provider.
            if (chosenProvider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
                ctx.EnvironmentVariables["LLM__ChatConnectionName"] = chatModel.Resource.Name;
        });

    if (!string.Equals(defaultEmbeddingModelId, defaultModelId, StringComparison.OrdinalIgnoreCase))
    {
        var embeddingModel = ollama.AddModel(defaultEmbeddingModelId);
        apiService
            .WithReference(embeddingModel)
            .WaitFor(embeddingModel)
            .WithEnvironment(ctx =>
            {
                if (chosenProvider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
                    ctx.EnvironmentVariables["LLM__EmbeddingConnectionName"] = embeddingModel.Resource.Name;
            });
    }
    else
    {
        apiService.WithEnvironment(ctx =>
        {
            if (chosenProvider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
                ctx.EnvironmentVariables["LLM__EmbeddingConnectionName"] = chatModel.Resource.Name;
        });
    }
}

// Prompt the user to configure the LLM provider and model interactively via the Aspire dashboard.
// The interaction fires before any resource starts, so the chosen values are available to the
// WithEnvironment callbacks above when the API service is launched.
builder.Eventing.Subscribe<BeforeStartEvent>(async (evt, ct) =>
{
    var interactionService = evt.Services.GetRequiredService<IInteractionService>();
    if (!interactionService.IsAvailable)
        return;

    var result = await interactionService.PromptInputsAsync(
        "LLM Configuration",
        "Configure the LLM provider and model for MattGPT. Dismiss to use defaults from configuration.",
        [
            new InteractionInput
            {
                Name = "provider",
                Label = "Provider",
                Description = "Select the LLM provider to use for chat and embeddings.",
                InputType = InputType.Choice,
                Options =
                [
                    KeyValuePair.Create("Ollama", "Ollama (local)"),
                    KeyValuePair.Create("FoundryLocal", "Foundry Local"),
                    KeyValuePair.Create("AzureOpenAI", "Azure OpenAI"),
                ],
                Value = chosenProvider,
                Required = true,
            },
            new InteractionInput
            {
                Name = "modelId",
                Label = "Chat Model ID",
                Description = "Model identifier for chat completions (e.g. llama3.2 for Ollama, or your Azure deployment name).",
                InputType = InputType.Text,
                Value = chosenModelId,
                Required = true,
            },
            new InteractionInput
            {
                Name = "embeddingModelId",
                Label = "Embedding Model ID",
                Description = "Model identifier for embeddings. Leave blank to reuse the chat model.",
                InputType = InputType.Text,
                Value = defaultEmbeddingModelId,
                Required = false,
            },
            new InteractionInput
            {
                Name = "endpoint",
                Label = "Endpoint URL",
                Description = "Base URL for the LLM API (required for FoundryLocal / AzureOpenAI, not used for Ollama).",
                InputType = InputType.Text,
                Value = chosenEndpoint,
                Placeholder = "https://your-endpoint.example.com",
                Required = false,
            },
            new InteractionInput
            {
                Name = "apiKey",
                Label = "API Key",
                Description = "API key for AzureOpenAI or FoundryLocal. Not required for Ollama.",
                InputType = InputType.SecretText,
                Required = false,
            },
        ],
        cancellationToken: ct);

    if (result.Canceled)
        return;

    var inputs = result.Data;
    chosenProvider = OrDefault(inputs["provider"].Value, chosenProvider);
    chosenModelId = OrDefault(inputs["modelId"].Value, chosenModelId);
    var embeddingEntry = inputs["embeddingModelId"].Value;
    chosenEmbeddingModelId = string.IsNullOrWhiteSpace(embeddingEntry) ? chosenModelId : embeddingEntry;
    chosenEndpoint = OrDefault(inputs["endpoint"].Value, chosenEndpoint);
    var apiKeyEntry = inputs["apiKey"].Value;
    if (!string.IsNullOrWhiteSpace(apiKeyEntry))
        chosenApiKey = apiKeyEntry;

    static string OrDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
});

builder.AddProject<Projects.MattGPT_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
