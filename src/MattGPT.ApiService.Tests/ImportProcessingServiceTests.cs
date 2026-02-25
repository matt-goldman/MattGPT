using System.Text;
using System.Threading.Channels;
using MattGPT.ApiService.Models;
using MattGPT.ApiService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace MattGPT.ApiService.Tests;

/// <summary>No-op repository used in unit tests that do not require MongoDB.</summary>
internal sealed class FakeConversationRepository : IConversationRepository
{
    public List<StoredConversation> Upserted { get; } = new();
    public List<(string Id, string? Summary, ConversationProcessingStatus Status)> SummaryUpdates { get; } = new();
    public List<(string Id, float[]? Embedding, ConversationProcessingStatus Status)> EmbeddingUpdates { get; } = new();

    private List<StoredConversation> _conversations = new();

    public void Seed(IEnumerable<StoredConversation> conversations)
        => _conversations.AddRange(conversations);

    public Task UpsertAsync(StoredConversation conversation, CancellationToken ct = default)
    {
        Upserted.Add(conversation);
        return Task.CompletedTask;
    }

    public Task<(List<StoredConversation> Items, long Total)> GetPageAsync(int page, int pageSize, CancellationToken ct = default)
        => Task.FromResult((new List<StoredConversation>(), 0L));

    public Task<List<StoredConversation>> GetByStatusAsync(ConversationProcessingStatus status, int maxCount, CancellationToken ct = default)
    {
        var items = _conversations
            .Where(c => c.ProcessingStatus == status)
            .Take(maxCount)
            .ToList();
        return Task.FromResult(items);
    }

    public Task<List<StoredConversation>> GetByStatusesAsync(IEnumerable<ConversationProcessingStatus> statuses, int maxCount, CancellationToken ct = default)
    {
        var statusSet = statuses.ToHashSet();
        var items = _conversations
            .Where(c => statusSet.Contains(c.ProcessingStatus))
            .Take(maxCount)
            .ToList();
        return Task.FromResult(items);
    }

    public Task UpdateSummaryAsync(string conversationId, string? summary, ConversationProcessingStatus status, CancellationToken ct = default)
    {
        SummaryUpdates.Add((conversationId, summary, status));
        var conv = _conversations.FirstOrDefault(c => c.ConversationId == conversationId);
        if (conv is not null)
        {
            conv.Summary = summary;
            conv.ProcessingStatus = status;
        }
        return Task.CompletedTask;
    }

    public Task UpdateEmbeddingAsync(string conversationId, float[]? embedding, ConversationProcessingStatus status, CancellationToken ct = default)
    {
        EmbeddingUpdates.Add((conversationId, embedding, status));
        var conv = _conversations.FirstOrDefault(c => c.ConversationId == conversationId);
        if (conv is not null)
        {
            conv.Embedding = embedding;
            conv.ProcessingStatus = status;
        }
        return Task.CompletedTask;
    }

    public Task<List<StoredConversation>> GetByIdsAsync(IEnumerable<string> conversationIds, CancellationToken ct = default)
    {
        var ids = conversationIds.ToHashSet();
        var items = _conversations.Where(c => ids.Contains(c.ConversationId)).ToList();
        return Task.FromResult(items);
    }

    public Task<Dictionary<ConversationProcessingStatus, long>> GetStatusCountsAsync(CancellationToken ct = default)
    {
        var counts = _conversations
            .GroupBy(c => c.ProcessingStatus)
            .ToDictionary(g => g.Key, g => (long)g.Count());
        return Task.FromResult(counts);
    }
}

public class ImportJobStoreTests
{
    [Fact]
    public void CreateJob_ReturnsJobWithQueuedStatus()
    {
        var store = new ImportJobStore();

        var job = store.CreateJob();

        Assert.NotNull(job);
        Assert.NotEmpty(job.JobId);
        Assert.Equal(ImportJobStatus.Queued, job.Status);
        Assert.Equal(0, job.ProcessedConversations);
        Assert.Equal(0, job.ErrorCount);
    }

    [Fact]
    public void GetJob_ExistingJob_ReturnsJob()
    {
        var store = new ImportJobStore();
        var job = store.CreateJob();

        var retrieved = store.GetJob(job.JobId);

        Assert.NotNull(retrieved);
        Assert.Equal(job.JobId, retrieved.JobId);
    }

    [Fact]
    public void GetJob_UnknownId_ReturnsNull()
    {
        var store = new ImportJobStore();

        var result = store.GetJob("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public void CreateJob_MultipleJobs_EachHasUniqueId()
    {
        var store = new ImportJobStore();

        var job1 = store.CreateJob();
        var job2 = store.CreateJob();

        Assert.NotEqual(job1.JobId, job2.JobId);
        Assert.NotNull(store.GetJob(job1.JobId));
        Assert.NotNull(store.GetJob(job2.JobId));
    }
}

public class ImportProcessingServiceTests
{
    /// <summary>Creates a minimal <see cref="IServiceProvider"/> for tests.
    /// Auto-embed will fail harmlessly because EmbeddingService is not registered.</summary>
    private static IServiceProvider EmptyServiceProvider()
        => new ServiceCollection().BuildServiceProvider();

    [Fact]
    public async Task ProcessesJob_UpdatesStatusToComplete()
    {
        // Arrange
        var channel = Channel.CreateBounded<ImportJobRequest>(10);
        var store = new ImportJobStore();
        var parser = new ConversationParser();
        var repository = new FakeConversationRepository();
        var logger = NullLogger<ImportProcessingService>.Instance;
        var service = new ImportProcessingService(channel, store, parser, repository, EmptyServiceProvider(), logger);

        var json = """
            [
              {
                "id": "c1", "title": "T1", "current_node": "n1",
                "mapping": {
                  "n1": { "id": "n1", "parent": null, "children": [],
                    "message": { "id": "n1", "author": { "role": "user" }, "content": { "content_type": "text" } }
                  }
                }
              }
            ]
            """;

        // Write JSON to a temp file.
        var tempPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempPath, json);

        var job = store.CreateJob();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        var runTask = service.StartAsync(cts.Token);
        await channel.Writer.WriteAsync(new ImportJobRequest(job.JobId, tempPath), cts.Token);

        // Wait until the job reaches a terminal state.
        while (job.Status is ImportJobStatus.Queued or ImportJobStatus.Processing)
        {
            await Task.Delay(50, cts.Token);
        }

        cts.Cancel();
        try { await runTask; } catch (OperationCanceledException) { }

        // Assert
        Assert.Equal(ImportJobStatus.Complete, job.Status);
        Assert.Equal(1, job.ProcessedConversations);
        Assert.Equal(0, job.ErrorCount);
        Assert.NotNull(job.CompletedAt);
    }

    [Fact]
    public async Task ProcessesJob_UpsertCalledForEachConversation()
    {
        // Arrange
        var channel = Channel.CreateBounded<ImportJobRequest>(10);
        var store = new ImportJobStore();
        var parser = new ConversationParser();
        var repository = new FakeConversationRepository();
        var logger = NullLogger<ImportProcessingService>.Instance;
        var service = new ImportProcessingService(channel, store, parser, repository, EmptyServiceProvider(), logger);

        var json = """
            [
              {
                "id": "c1", "title": "T1", "current_node": "n1",
                "mapping": {
                  "n1": { "id": "n1", "parent": null, "children": [],
                    "message": { "id": "n1", "author": { "role": "user" }, "content": { "content_type": "text" } }
                  }
                }
              },
              {
                "id": "c2", "title": "T2", "current_node": "n2",
                "mapping": {
                  "n2": { "id": "n2", "parent": null, "children": [],
                    "message": { "id": "n2", "author": { "role": "assistant" }, "content": { "content_type": "text" } }
                  }
                }
              }
            ]
            """;

        var tempPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempPath, json);

        var job = store.CreateJob();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        var runTask = service.StartAsync(cts.Token);
        await channel.Writer.WriteAsync(new ImportJobRequest(job.JobId, tempPath), cts.Token);

        while (job.Status is ImportJobStatus.Queued or ImportJobStatus.Processing)
        {
            await Task.Delay(50, cts.Token);
        }

        cts.Cancel();
        try { await runTask; } catch (OperationCanceledException) { }

        // Assert — repository should have received one upsert per conversation.
        Assert.Equal(2, repository.Upserted.Count);
        Assert.Contains(repository.Upserted, c => c.ConversationId == "c1");
        Assert.Contains(repository.Upserted, c => c.ConversationId == "c2");
        Assert.All(repository.Upserted, c => Assert.Equal(ConversationProcessingStatus.Imported, c.ProcessingStatus));
    }

    [Fact]
    public async Task ProcessesJob_InvalidFile_SetsStatusToFailed()
    {
        // Arrange
        var channel = Channel.CreateBounded<ImportJobRequest>(10);
        var store = new ImportJobStore();
        var parser = new ConversationParser();
        var repository = new FakeConversationRepository();
        var logger = NullLogger<ImportProcessingService>.Instance;
        var service = new ImportProcessingService(channel, store, parser, repository, EmptyServiceProvider(), logger);

        var tempPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempPath, "NOT VALID JSON {{{{");

        var job = store.CreateJob();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        var runTask = service.StartAsync(cts.Token);
        await channel.Writer.WriteAsync(new ImportJobRequest(job.JobId, tempPath), cts.Token);

        while (job.Status is ImportJobStatus.Queued or ImportJobStatus.Processing)
        {
            await Task.Delay(50, cts.Token);
        }

        cts.Cancel();
        try { await runTask; } catch (OperationCanceledException) { }

        // Assert
        Assert.Equal(ImportJobStatus.Failed, job.Status);
        Assert.NotNull(job.ErrorMessage);
        Assert.NotNull(job.CompletedAt);
    }

    [Fact]
    public async Task ProcessesJob_TempFileDeletedAfterProcessing()
    {
        // Arrange
        var channel = Channel.CreateBounded<ImportJobRequest>(10);
        var store = new ImportJobStore();
        var parser = new ConversationParser();
        var repository = new FakeConversationRepository();
        var logger = NullLogger<ImportProcessingService>.Instance;
        var service = new ImportProcessingService(channel, store, parser, repository, EmptyServiceProvider(), logger);

        var tempPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(tempPath, "[]");

        var job = store.CreateJob();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        // Act
        var runTask = service.StartAsync(cts.Token);
        await channel.Writer.WriteAsync(new ImportJobRequest(job.JobId, tempPath), cts.Token);

        while (job.Status is ImportJobStatus.Queued or ImportJobStatus.Processing)
        {
            await Task.Delay(50, cts.Token);
        }

        cts.Cancel();
        try { await runTask; } catch (OperationCanceledException) { }

        // Assert
        Assert.False(File.Exists(tempPath), "Temp file should have been deleted after processing.");
    }
}
