using System.Threading.Channels;
using MattGPT.ApiService.Models;
using MattGPT.ApiService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MattGPT.ApiService.Tests;

public class UserProfileTests
{
    // ---- BuildMessages tests ----

    [Fact]
    public void BuildMessages_WithUserProfile_IncludesProfileInSystemMessage()
    {
        var profile = new UserProfile
        {
            UserProfileText = "I am a software engineer.",
            UserInstructions = "Be concise.",
        };

        var messages = RagService.BuildMessages("hello", [], userProfile: profile);

        var systemText = messages.First(m => m.Role == Microsoft.Extensions.AI.ChatRole.System).Text ?? string.Empty;
        Assert.Contains("I am a software engineer.", systemText);
        Assert.Contains("=== USER CONTEXT ===", systemText);
    }

    [Fact]
    public void BuildMessages_WithUserInstructions_IncludesInstructionsInSystemMessage()
    {
        var profile = new UserProfile
        {
            UserInstructions = "Respond in bullet points.",
        };

        var messages = RagService.BuildMessages("hello", [], userProfile: profile);

        var systemText = messages.First(m => m.Role == Microsoft.Extensions.AI.ChatRole.System).Text ?? string.Empty;
        Assert.Contains("Respond in bullet points.", systemText);
        Assert.Contains("User's preferences for responses:", systemText);
    }

    [Fact]
    public void BuildMessages_WithNullUserProfile_NoProfileSection()
    {
        var messages = RagService.BuildMessages("hello", [], userProfile: null);

        var systemText = messages.First(m => m.Role == Microsoft.Extensions.AI.ChatRole.System).Text ?? string.Empty;
        Assert.DoesNotContain("=== USER CONTEXT ===", systemText);
    }

    [Fact]
    public void BuildMessages_WithEmptyUserProfile_NoProfileSection()
    {
        var profile = new UserProfile
        {
            UserProfileText = "   ",
            UserInstructions = null,
        };

        var messages = RagService.BuildMessages("hello", [], userProfile: profile);

        var systemText = messages.First(m => m.Role == Microsoft.Extensions.AI.ChatRole.System).Text ?? string.Empty;
        Assert.DoesNotContain("=== USER CONTEXT ===", systemText);
    }

    // ---- Import extraction tests ----

    private static IServiceProvider EmptyServiceProvider()
        => new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task Import_ExtractsUserProfile_FromUserEditableContextMessage()
    {
        var channel = Channel.CreateBounded<ImportJobRequest>(10);
        var store = new ImportJobStore();
        var parser = new ConversationParser();
        var repository = new FakeConversationRepository();
        var profileRepo = new FakeUserProfileRepository();
        var logger = NullLogger<ImportProcessingService>.Instance;
        var service = new ImportProcessingService(channel, store, parser, repository, profileRepo, EmptyServiceProvider(), logger);

        var json = """
            [
              {
                "id": "c1", "title": "T1", "current_node": "n2",
                "mapping": {
                  "n1": { "id": "n1", "parent": null, "children": ["n2"],
                    "message": {
                      "id": "n1", "author": { "role": "system" },
                      "create_time": 1000.0,
                      "content": {
                        "content_type": "user_editable_context",
                        "user_profile": "I am a developer.",
                        "user_instructions": "Be brief."
                      },
                      "metadata": { "is_visually_hidden_from_conversation": true }
                    }
                  },
                  "n2": { "id": "n2", "parent": "n1", "children": [],
                    "message": { "id": "n2", "author": { "role": "user" }, "create_time": 2000.0, "content": { "content_type": "text", "parts": ["hello"] } }
                  }
                }
              }
            ]
            """;

        var tempPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempPath, json);

        var job = store.CreateJob();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await service.StartAsync(cts.Token);
        await channel.Writer.WriteAsync(new ImportJobRequest(job.JobId, tempPath), cts.Token);

        while (job.Status is ImportJobStatus.Queued or ImportJobStatus.Processing)
            await Task.Delay(50, cts.Token);

        cts.Cancel();
        try { await service.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }

        Assert.Equal(ImportJobStatus.Complete, job.Status);
        Assert.NotNull(profileRepo.Stored);
        Assert.Equal("I am a developer.", profileRepo.Stored.UserProfileText);
        Assert.Equal("Be brief.", profileRepo.Stored.UserInstructions);
        Assert.Equal(1000.0, profileRepo.Stored.SourceCreateTime);
    }

    [Fact]
    public async Task Import_TracksLatestUserProfile_AcrossMultipleConversations()
    {
        var channel = Channel.CreateBounded<ImportJobRequest>(10);
        var store = new ImportJobStore();
        var parser = new ConversationParser();
        var repository = new FakeConversationRepository();
        var profileRepo = new FakeUserProfileRepository();
        var logger = NullLogger<ImportProcessingService>.Instance;
        var service = new ImportProcessingService(channel, store, parser, repository, profileRepo, EmptyServiceProvider(), logger);

        // Two conversations each with a user_editable_context; second has newer create_time.
        var json = """
            [
              {
                "id": "c1", "title": "T1", "current_node": "n1",
                "mapping": {
                  "n1": { "id": "n1", "parent": null, "children": [],
                    "message": {
                      "id": "n1", "author": { "role": "system" }, "create_time": 500.0,
                      "content": { "content_type": "user_editable_context", "user_profile": "Old profile.", "user_instructions": "Old instructions." }
                    }
                  }
                }
              },
              {
                "id": "c2", "title": "T2", "current_node": "n2",
                "mapping": {
                  "n2": { "id": "n2", "parent": null, "children": [],
                    "message": {
                      "id": "n2", "author": { "role": "system" }, "create_time": 2000.0,
                      "content": { "content_type": "user_editable_context", "user_profile": "New profile.", "user_instructions": "New instructions." }
                    }
                  }
                }
              }
            ]
            """;

        var tempPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempPath, json);

        var job = store.CreateJob();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await service.StartAsync(cts.Token);
        await channel.Writer.WriteAsync(new ImportJobRequest(job.JobId, tempPath), cts.Token);

        while (job.Status is ImportJobStatus.Queued or ImportJobStatus.Processing)
            await Task.Delay(50, cts.Token);

        cts.Cancel();
        try { await service.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }

        Assert.Equal(ImportJobStatus.Complete, job.Status);
        Assert.NotNull(profileRepo.Stored);
        Assert.Equal("New profile.", profileRepo.Stored.UserProfileText);
        Assert.Equal(2000.0, profileRepo.Stored.SourceCreateTime);
    }

    [Fact]
    public async Task Import_NoUserEditableContext_ProfileRemainsNull()
    {
        var channel = Channel.CreateBounded<ImportJobRequest>(10);
        var store = new ImportJobStore();
        var parser = new ConversationParser();
        var repository = new FakeConversationRepository();
        var profileRepo = new FakeUserProfileRepository();
        var logger = NullLogger<ImportProcessingService>.Instance;
        var service = new ImportProcessingService(channel, store, parser, repository, profileRepo, EmptyServiceProvider(), logger);

        var json = """
            [
              {
                "id": "c1", "title": "T1", "current_node": "n1",
                "mapping": {
                  "n1": { "id": "n1", "parent": null, "children": [],
                    "message": { "id": "n1", "author": { "role": "user" }, "content": { "content_type": "text", "parts": ["hello"] } }
                  }
                }
              }
            ]
            """;

        var tempPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempPath, json);

        var job = store.CreateJob();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await service.StartAsync(cts.Token);
        await channel.Writer.WriteAsync(new ImportJobRequest(job.JobId, tempPath), cts.Token);

        while (job.Status is ImportJobStatus.Queued or ImportJobStatus.Processing)
            await Task.Delay(50, cts.Token);

        cts.Cancel();
        try { await service.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }

        Assert.Equal(ImportJobStatus.Complete, job.Status);
        Assert.Null(profileRepo.Stored);
    }
}
