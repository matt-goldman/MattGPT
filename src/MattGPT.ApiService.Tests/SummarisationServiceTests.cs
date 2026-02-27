using MattGPT.ApiService.Models;
using MattGPT.ApiService.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace MattGPT.ApiService.Tests;

/// <summary>
/// Fake IChatClient that returns a predefined reply or throws on demand.
/// </summary>
internal sealed class FakeChatClient(Func<IEnumerable<ChatMessage>, ChatResponse> handler) : IChatClient
{
    public FakeChatClient(string reply)
        : this(_ => new ChatResponse(new ChatMessage(ChatRole.Assistant, reply))) { }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(handler(messages));

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? key = null) => null;

    public void Dispose() { }
}

/// <summary>
/// Fake IChatClient that always throws an exception.
/// </summary>
internal sealed class ThrowingChatClient(Exception exception) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw exception;

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException();

    public object? GetService(Type serviceType, object? key = null) => null;

    public void Dispose() { }
}

public class SummarisationServiceTests
{
    private static StoredConversation MakeConversation(string id, string title = "Test", int messageCount = 2)
    {
        var messages = Enumerable.Range(0, messageCount)
            .Select(i => new StoredMessage
            {
                Id = $"m{i}",
                Role = i % 2 == 0 ? "user" : "assistant",
                ContentType = "text",
                Parts = [$"Message content {i}"],
            })
            .ToList();

        return new StoredConversation
        {
            ConversationId = id,
            Title = title,
            LinearisedMessages = messages,
            ProcessingStatus = ConversationProcessingStatus.Imported,
        };
    }

    [Fact]
    public async Task SummariseAsync_UpdatesStatusToSummarised()
    {
        var repository = new FakeConversationRepository();
        var conversation = MakeConversation("c1");
        repository.Seed([conversation]);

        var chatClient = new FakeChatClient("This is a test summary.");
        var service = new SummarisationService(repository, chatClient, NullLogger<SummarisationService>.Instance);

        var result = await service.SummariseAsync();

        Assert.Equal(1, result.Summarised);
        Assert.Equal(0, result.Errors);
        Assert.Single(repository.SummaryUpdates);
        Assert.Equal("c1", repository.SummaryUpdates[0].Id);
        Assert.Equal("This is a test summary.", repository.SummaryUpdates[0].Summary);
        Assert.Equal(ConversationProcessingStatus.Summarised, repository.SummaryUpdates[0].Status);
    }

    [Fact]
    public async Task SummariseAsync_LlmError_MarksAsSummaryError()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([MakeConversation("c1")]);

        var chatClient = new ThrowingChatClient(new InvalidOperationException("LLM unavailable"));
        var service = new SummarisationService(repository, chatClient, NullLogger<SummarisationService>.Instance);

        var result = await service.SummariseAsync();

        Assert.Equal(0, result.Summarised);
        Assert.Equal(1, result.Errors);
        Assert.Single(repository.SummaryUpdates);
        Assert.Equal(ConversationProcessingStatus.SummaryError, repository.SummaryUpdates[0].Status);
    }

    [Fact]
    public async Task SummariseAsync_NoMessages_MarksAsSummarisedSkipped()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([MakeConversation("c1", messageCount: 0)]);

        var chatClient = new FakeChatClient("Should not be called");
        var service = new SummarisationService(repository, chatClient, NullLogger<SummarisationService>.Instance);

        var result = await service.SummariseAsync();

        Assert.Equal(0, result.Summarised);
        Assert.Equal(0, result.Errors);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(ConversationProcessingStatus.Summarised, repository.SummaryUpdates[0].Status);
        Assert.Null(repository.SummaryUpdates[0].Summary);
    }

    [Fact]
    public async Task SummariseAsync_MultipleConversations_AllProcessed()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([
            MakeConversation("c1"),
            MakeConversation("c2"),
            MakeConversation("c3"),
        ]);

        var callCount = 0;
        var chatClient = new FakeChatClient(_ =>
        {
            callCount++;
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, $"Summary {callCount}"));
        });

        var service = new SummarisationService(repository, chatClient, NullLogger<SummarisationService>.Instance);

        var result = await service.SummariseAsync();

        Assert.Equal(3, result.Summarised);
        Assert.Equal(0, result.Errors);
        Assert.Equal(3, callCount);
    }

    [Fact]
    public async Task SummariseAsync_ErrorDoesNotAbortBatch()
    {
        var repository = new FakeConversationRepository();
        repository.Seed([
            MakeConversation("c1"),
            MakeConversation("c2"),
            MakeConversation("c3"),
        ]);

        int callCount = 0;
        var chatClient = new FakeChatClient(_ =>
        {
            callCount++;
            // Fail only the second call.
            if (callCount == 2)
                throw new InvalidOperationException("LLM error on second call");
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, $"Summary {callCount}"));
        });

        var service = new SummarisationService(repository, chatClient, NullLogger<SummarisationService>.Instance);

        var result = await service.SummariseAsync();

        Assert.Equal(2, result.Summarised);
        Assert.Equal(1, result.Errors);
        Assert.Equal(3, callCount);
        Assert.Equal(3, repository.SummaryUpdates.Count);
    }

    [Fact]
    public async Task SummariseAsync_EmptyRepository_ReturnsZeroCounts()
    {
        var repository = new FakeConversationRepository();
        var chatClient = new FakeChatClient("Should not be called");
        var service = new SummarisationService(repository, chatClient, NullLogger<SummarisationService>.Instance);

        var result = await service.SummariseAsync();

        Assert.Equal(0, result.Summarised);
        Assert.Equal(0, result.Errors);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void BuildPrompt_IncludesTitleAndMessages()
    {
        var conversation = MakeConversation("c1", "My Test Conversation");

        var prompt = SummarisationService.BuildPrompt(conversation);

        Assert.Contains("My Test Conversation", prompt);
        Assert.Contains("user:", prompt);
        Assert.Contains("assistant:", prompt);
    }

    [Fact]
    public void BuildPrompt_LongConversation_Truncates()
    {
        // Create a conversation with many long messages to force truncation.
        var messages = Enumerable.Range(0, 500)
            .Select(i => new StoredMessage
            {
                Id = $"m{i}",
                Role = "user",
                ContentType = "text",
                Parts = [new string('x', 200)],
            })
            .ToList();

        var conversation = new StoredConversation
        {
            ConversationId = "big",
            Title = "Big conversation",
            LinearisedMessages = messages,
            ProcessingStatus = ConversationProcessingStatus.Imported,
        };

        var prompt = SummarisationService.BuildPrompt(conversation);

        Assert.NotNull(prompt);
        Assert.Contains("truncated", prompt);
        // Prompt should not be excessively large.
        Assert.True(prompt.Length < 15_000);
    }

    [Fact]
    public void BuildPrompt_ExcludesZeroWeightMessages()
    {
        var conversation = new StoredConversation
        {
            ConversationId = "c1",
            Title = "Test",
            LinearisedMessages =
            [
                new StoredMessage { Id = "m0", Role = "system", ContentType = "text", Parts = ["System scaffolding"], Weight = 0.0 },
                new StoredMessage { Id = "m1", Role = "user", ContentType = "text", Parts = ["User question"], Weight = 1.0 },
                new StoredMessage { Id = "m2", Role = "assistant", ContentType = "text", Parts = ["Assistant answer"], Weight = 1.0 },
            ],
            ProcessingStatus = ConversationProcessingStatus.Imported,
        };

        var prompt = SummarisationService.BuildPrompt(conversation);

        Assert.DoesNotContain("System scaffolding", prompt);
        Assert.Contains("user: User question", prompt);
        Assert.Contains("assistant: Assistant answer", prompt);
    }

    [Fact]
    public void BuildPrompt_ExcludesHiddenMessages()
    {
        var conversation = new StoredConversation
        {
            ConversationId = "c1",
            Title = "Test",
            LinearisedMessages =
            [
                new StoredMessage { Id = "m0", Role = "system", ContentType = "user_editable_context", Parts = ["[User Profile] my profile"], IsHidden = true },
                new StoredMessage { Id = "m1", Role = "user", ContentType = "text", Parts = ["Visible question"] },
                new StoredMessage { Id = "m2", Role = "assistant", ContentType = "text", Parts = ["Visible answer"] },
            ],
            ProcessingStatus = ConversationProcessingStatus.Imported,
        };

        var prompt = SummarisationService.BuildPrompt(conversation);

        Assert.DoesNotContain("User Profile", prompt);
        Assert.Contains("user: Visible question", prompt);
        Assert.Contains("assistant: Visible answer", prompt);
    }

    [Fact]
    public void BuildPrompt_NullWeight_IncludesMessage()
    {
        var conversation = new StoredConversation
        {
            ConversationId = "c1",
            Title = "Test",
            LinearisedMessages =
            [
                new StoredMessage { Id = "m1", Role = "user", ContentType = "text", Parts = ["Included"], Weight = null },
            ],
            ProcessingStatus = ConversationProcessingStatus.Imported,
        };

        var prompt = SummarisationService.BuildPrompt(conversation);

        Assert.NotNull(prompt);
        Assert.Contains("user: Included", prompt);
    }

    [Fact]
    public void BuildPrompt_AllMessagesFiltered_ReturnsNull()
    {
        var conversation = new StoredConversation
        {
            ConversationId = "c1",
            Title = "Test",
            LinearisedMessages =
            [
                new StoredMessage { Id = "m0", Role = "system", ContentType = "text", Parts = ["System scaffolding"], Weight = 0.0 },
                new StoredMessage { Id = "m1", Role = "system", ContentType = "user_editable_context", Parts = ["[User Profile] ..."], IsHidden = true },
            ],
            ProcessingStatus = ConversationProcessingStatus.Imported,
        };

        var prompt = SummarisationService.BuildPrompt(conversation);

        Assert.Null(prompt);
    }

    [Fact]
    public async Task SummariseAsync_AllMessagesHidden_MarksAsSkipped()
    {
        var repository = new FakeConversationRepository();
        var conversation = new StoredConversation
        {
            ConversationId = "c1",
            Title = "All hidden",
            LinearisedMessages =
            [
                new StoredMessage { Id = "m0", Role = "system", ContentType = "text", Parts = ["Hidden"], Weight = 0.0 },
                new StoredMessage { Id = "m1", Role = "system", ContentType = "text", Parts = ["Also hidden"], IsHidden = true },
            ],
            ProcessingStatus = ConversationProcessingStatus.Imported,
        };
        repository.Seed([conversation]);

        var chatClient = new FakeChatClient("Should not be called");
        var service = new SummarisationService(repository, chatClient, NullLogger<SummarisationService>.Instance);

        var result = await service.SummariseAsync();

        Assert.Equal(0, result.Summarised);
        Assert.Equal(0, result.Errors);
        Assert.Equal(1, result.Skipped);
        Assert.Single(repository.SummaryUpdates);
        Assert.Equal(ConversationProcessingStatus.Summarised, repository.SummaryUpdates[0].Status);
        Assert.Null(repository.SummaryUpdates[0].Summary);
    }
}
