using MattGPT.ApiService.Endpoints;
using MattGPT.ApiService.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel for large file uploads (up to 250 MB).
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 262_144_000; // 250 MB
});

// --- Azure App Configuration ---
// Uses the Aspire client integration which automatically handles emulator connections
// (anonymous auth) and production connections (DefaultAzureCredential) based on the
// connection string injected by the AppHost.
builder.AddAzureAppConfiguration("appconfig");

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add specified auth provider if enabled in config
builder.AddOptionalAuthentication();

builder.AddDocumentStorage();

builder.AddApplicationServices();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.AddAiProvider();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

app.UseOptionalAuthentication();

app.MapConversationsEndpoints();
app.MapSearchEndpoints();
app.MapChatEndpoints();
app.MapSettingsEndpoints();
app.MapDiagnosticsEndpoints();

app.MapDefaultEndpoints();

app.MapOpenApi();

app.Run();
