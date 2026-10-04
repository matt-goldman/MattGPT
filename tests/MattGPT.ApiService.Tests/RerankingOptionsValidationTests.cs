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

    private static readonly LlmOptions ValidRerankingLlm = new() { RerankingModelId = "rerank-v3.5" };

    [Theory]
    [InlineData(null)]
    [InlineData("Cohere")]
    [InlineData("cohere")]
    public void RerankingProvider_SupportedOrUnset_IsValid(string? provider)
    {
        var llm = new LlmOptions { RerankingModelId = "m", RerankingProvider = provider };

        AiExtensions.ValidateRerankingOptions(new RagOptions { UseReranking = true }, llm);
    }

    [Fact]
    public void RerankingProvider_Unsupported_Throws()
    {
        var llm = new LlmOptions { RerankingModelId = "m", RerankingProvider = "Ollama" };

        var ex = Assert.Throws<InvalidOperationException>(
            () => AiExtensions.ValidateRerankingOptions(new RagOptions { UseReranking = true }, llm));
        Assert.Contains("LLM:RerankingProvider", ex.Message);
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("/v1/rerank")]
    [InlineData("ftp://example.com/rerank")]
    public void RerankingEndpoint_Invalid_Throws(string endpoint)
    {
        var llm = new LlmOptions { RerankingModelId = "m", RerankingEndpoint = endpoint };

        var ex = Assert.Throws<InvalidOperationException>(
            () => AiExtensions.ValidateRerankingOptions(new RagOptions { UseReranking = true }, llm));
        Assert.Contains("LLM:RerankingEndpoint", ex.Message);
    }

    [Fact]
    public void RerankingEndpoint_AbsoluteHttpUrl_IsValid()
    {
        var llm = new LlmOptions { RerankingModelId = "m", RerankingEndpoint = "http://localhost:8000/v1/rerank" };

        AiExtensions.ValidateRerankingOptions(new RagOptions { UseReranking = true }, llm);
    }

    [Theory]
    [InlineData(0, 4000, "RAG:RerankCandidateCount")]
    [InlineData(30, 0, "RAG:RerankDocumentChars")]
    public void RerankTuning_NotPositive_Throws(int candidates, int docChars, string expectedKey)
    {
        var rag = new RagOptions { UseReranking = true, RerankCandidateCount = candidates, RerankDocumentChars = docChars };

        var ex = Assert.Throws<InvalidOperationException>(() => AiExtensions.ValidateRerankingOptions(rag, ValidRerankingLlm));
        Assert.Contains(expectedKey, ex.Message);
    }

    [Fact]
    public void RerankingSettings_NotValidated_WhenRerankingDisabled()
    {
        // Stale reranking config must not block startup when reranking is off.
        var rag = new RagOptions { UseReranking = false, RerankCandidateCount = 0 };
        var llm = new LlmOptions { RerankingProvider = "Ollama", RerankingEndpoint = "not a url" };

        AiExtensions.ValidateRerankingOptions(rag, llm);
    }
}
