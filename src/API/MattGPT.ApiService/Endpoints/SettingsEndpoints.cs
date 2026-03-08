using MattGPT.Contracts.Models;
using MattGPT.ApiService.Services;
using MattGPT.Contracts.Services;

namespace MattGPT.ApiService.Endpoints;

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        // GET current user profile.
        app.MapGet("/user-profile", async (IUserProfileRepository profileRepo, CancellationToken ct) =>
        {
            var profile = await profileRepo.GetAsync(ct);
            return Results.Ok(new
            {
                userProfileText = profile?.UserProfileText,
                userInstructions = profile?.UserInstructions,
                lastUpdated = profile?.LastUpdated,
            });
        })
        .WithName("GetUserProfile");

        // PUT update user profile.
        app.MapPut("/user-profile", async (UserProfileRequest request, IUserProfileRepository profileRepo, CancellationToken ct) =>
        {
            var existing = await profileRepo.GetAsync(ct) ?? new UserProfile();
            existing.UserProfileText = request.UserProfileText;
            existing.UserInstructions = request.UserInstructions;
            existing.LastUpdated = DateTimeOffset.UtcNow;
            await profileRepo.UpsertAsync(existing, ct);
            return Results.Ok(new
            {
                userProfileText = existing.UserProfileText,
                userInstructions = existing.UserInstructions,
                lastUpdated = existing.LastUpdated,
            });
        })
        .WithName("UpdateUserProfile");

        // GET current system prompt.
        app.MapGet("/system-prompt", async (ISystemConfigRepository configRepo, CancellationToken ct) =>
        {
            var config = await configRepo.GetAsync(ct);
            return Results.Ok(new
            {
                systemPrompt = config?.SystemPrompt ?? RagService.DefaultSystemPrompt,
                isDefault = config?.SystemPrompt is null,
                lastUpdated = config?.LastUpdated,
            });
        })
        .WithName("GetSystemPrompt");

        // PUT update system prompt.
        app.MapPut("/system-prompt", async (SystemPromptRequest request, ISystemConfigRepository configRepo, CancellationToken ct) =>
        {
            var existing = await configRepo.GetAsync(ct) ?? new SystemConfig();
            existing.SystemPrompt = string.IsNullOrWhiteSpace(request.SystemPrompt) ? null : request.SystemPrompt.Trim();
            existing.LastUpdated = DateTimeOffset.UtcNow;
            await configRepo.UpsertAsync(existing, ct);
            return Results.Ok(new
            {
                systemPrompt = existing.SystemPrompt ?? RagService.DefaultSystemPrompt,
                isDefault = existing.SystemPrompt is null,
                lastUpdated = existing.LastUpdated,
            });
        })
        .WithName("UpdateSystemPrompt");

        // DELETE system prompt — resets to default.
        app.MapDelete("/system-prompt", async (ISystemConfigRepository configRepo, CancellationToken ct) =>
        {
            var existing = await configRepo.GetAsync(ct);
            if (existing is not null)
            {
                existing.SystemPrompt = null;
                existing.LastUpdated = DateTimeOffset.UtcNow;
                await configRepo.UpsertAsync(existing, ct);
            }
            return Results.Ok(new
            {
                systemPrompt = RagService.DefaultSystemPrompt,
                isDefault = true,
            });
        })
        .WithName("ResetSystemPrompt");

        return app;
    }
}

public record UserProfileRequest(string? UserProfileText, string? UserInstructions);
public record SystemPromptRequest(string? SystemPrompt);
