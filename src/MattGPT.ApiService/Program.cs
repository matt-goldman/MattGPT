var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

// Add MongoDB client via Aspire integration.
builder.AddMongoDBClient("mattgptdb");

// Add services to the container.
builder.Services.AddProblemDetails();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

app.MapGet("/", () => "API service is running. Navigate to /weatherforecast to see sample data.");

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

// Stub endpoint for conversation file upload (full processing wired in issue 006).
app.MapPost("/conversations/upload", async (HttpRequest request) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Expected multipart/form-data.");

    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");

    if (file is null)
        return Results.BadRequest("No file provided.");

    if (!file.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest("Only .json files are accepted.");

    // Drain the stream to simulate receiving the full file.
    await using var stream = file.OpenReadStream();
    await stream.CopyToAsync(Stream.Null);

    return Results.Accepted("/conversations/upload", new { message = "File received. Processing will begin shortly.", fileName = file.FileName, sizeBytes = file.Length });
})
.WithName("UploadConversations")
.DisableAntiforgery();


app.MapDefaultEndpoints();

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
