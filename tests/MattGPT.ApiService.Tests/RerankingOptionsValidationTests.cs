using MattGPT.ApiService.Extensions;
using MattGPT.Contracts;

namespace MattGPT.ApiService.Tests;

public class RerankingOptionsValidationTests
{
    [Fact]
    public void UseReranking_WithModel_IsValid()
    {
        var rag = new RagOptions { UseReranking = true };
        var llm = new LlmOptions { RerankingModelId = "bge-reranker-v2-m3" };

        AiExtensions.ValidateRerankingOptions(rag, llm);
    }

    [Fact]
    public void RerankingDisabled_WithoutModel_IsValid()
    {
        var rag = new RagOptions { UseReranking = false };

        AiExtensions.ValidateRerankingOptions(rag, new LlmOptions());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UseReranking_WithoutModel_Throws(string? modelId)
    {
        var rag = new RagOptions { UseReranking = true };
        var llm = new LlmOptions { RerankingModelId = modelId };

        var ex = Assert.Throws<InvalidOperationException>(() => AiExtensions.ValidateRerankingOptions(rag, llm));
        Assert.Contains("LLM:RerankingModelId", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MaxEmbeddingChars_NotPositive_Throws(int maxChars)
    {
        var rag = new RagOptions { UseReranking = false, MaxEmbeddingChars = maxChars };

        var ex = Assert.Throws<InvalidOperationException>(() => AiExtensions.ValidateRerankingOptions(rag, new LlmOptions()));
        Assert.Contains("RAG:MaxEmbeddingChars", ex.Message);
    }

    [Fact]
    public void MaxEmbeddingChars_Positive_IsValid()
    {
        var rag = new RagOptions { UseReranking = false, MaxEmbeddingChars = 16_000 };

        AiExtensions.ValidateRerankingOptions(rag, new LlmOptions());
    }

    [Fact]
    public void MultipleErrors_AreAllReported()
    {
        var rag = new RagOptions { UseReranking = true, MaxEmbeddingChars = 0 };

        var ex = Assert.Throws<InvalidOperationException>(() => AiExtensions.ValidateRerankingOptions(rag, new LlmOptions()));
        Assert.Contains("LLM:RerankingModelId", ex.Message);
        Assert.Contains("RAG:MaxEmbeddingChars", ex.Message);
    }
}
