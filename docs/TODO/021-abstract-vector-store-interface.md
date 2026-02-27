# 021 — Abstract Vector Store Behind Provider-Agnostic Interface

**Status:** TODO
**Sequence:** 21
**Dependencies:** 002 (Qdrant integration), 010 (embeddings in Qdrant)

## Summary

Replace the Qdrant-specific `IQdrantService` interface with a provider-agnostic `IVectorStore` abstraction, enabling future support for alternative vector databases (e.g., Milvus, Weaviate, Chroma, or an in-memory store for testing) via configuration — analogous to how `LlmOptions` already supports multiple LLM providers.

## Problem

The current `IQdrantService` / `QdrantService` is tightly coupled to the Qdrant SDK. Every consumer (`RagService`, `EmbeddingService`) references Qdrant-specific types like `QdrantSearchResult`. This means:
- Swapping to a different vector database requires changing all call sites.
- Unit tests use a hand-rolled `FakeQdrantService` that mirrors the Qdrant-shaped interface.
- The naming is misleading in a codebase that otherwise uses provider-agnostic abstractions (e.g., `IChatClient`, `IEmbeddingGenerator`).

There is an existing TODO comment in `QdrantService.cs` noting this.

## Design

1. Define an `IVectorStore` interface with methods like `UpsertAsync`, `SearchAsync`, `GetPointCountAsync` using provider-neutral types (e.g., `VectorSearchResult` instead of `QdrantSearchResult`).
2. `QdrantVectorStore` becomes one implementation; others can follow.
3. Add a `VectorStore:Provider` configuration key (analogous to `LLM:Provider`) that selects the concrete implementation at startup.
4. Update `RagService`, `EmbeddingService`, and all consumers to depend on `IVectorStore`.
5. Update test fakes to implement `IVectorStore`.

## Requirements

1. Define `IVectorStore` and generic result types.
2. Refactor `QdrantService` into `QdrantVectorStore : IVectorStore`.
3. Update all consumers and test fakes.
4. Add config-driven provider selection in `Program.cs`.
5. All existing tests must continue to pass.

## Acceptance Criteria

- [ ] A provider-agnostic `IVectorStore` interface exists and is the only vector store abstraction referenced by business logic.
- [ ] `QdrantVectorStore` implements `IVectorStore` and preserves all current behaviour.
- [ ] The active vector store provider is selected via configuration.
- [ ] All existing unit tests pass with updated fakes.
- [ ] Adding a new vector store provider requires only a new implementation class and a `case` in the startup switch.

## Notes

- Consider whether `Microsoft.Extensions.AI` or Semantic Kernel will ship a standard `IVectorStore` abstraction. If one materialises before this issue is picked up, prefer adopting it over rolling our own. Check the latest .NET + AI packages.
- This is low-urgency — Qdrant works well and there's no immediate need to swap providers. It's a code-quality / extensibility improvement.
- An ADR may be warranted if the interface shape involves non-obvious trade-offs.
