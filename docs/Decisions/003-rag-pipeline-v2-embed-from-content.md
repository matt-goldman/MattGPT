# ADR-003: RAG Pipeline v2 — Embed from Content, Fix Prompt & Timeouts

**Date:** 2026-02-26
**Status:** Accepted
**Related Issues:** 005, 008, 009, 011, 012

## Context

After the initial end-to-end pipeline was complete (issues 001–013), real-world testing on a ~2,213-conversation ChatGPT export revealed several problems:

1. **RAG responses ignored conversation context.** The original `RagService` sent only summaries as flat strings in a single user message. The LLM treated them as part of the user's question rather than reference material, and often responded with generic answers.

2. **LLM summarisation was prohibitively slow.** Generating summaries via Ollama llama3.2 on CPU took ~1 minute per conversation. At 2,213 conversations this would take ~37 hours, making the pipeline impractical for local/offline use.

3. **Embedding required summarisation to complete first.** The `EmbeddingService` only processed conversations with status `Summarised`, meaning the entire slow summarisation step had to finish before any conversation was searchable.

4. **Ollama HttpClient timeouts during chat.** The default 100-second `HttpClient.Timeout` was too short for llama3.2 running on CPU inside Docker (no GPU passthrough). Large RAG prompts (~16K chars) routinely exceeded this, causing 500 errors.

5. **Upload UI had no visibility into embedding progress.** After import completed, the UI showed "Complete" even though the auto-embedding phase was still running in the background.

## Decision

### 1. Embed directly from conversation content (bypass summarisation)

`EmbeddingService` was rewritten to generate embeddings from conversation content (title + optional summary + message text, up to 8,000 chars) rather than requiring an LLM-generated summary. It now processes conversations in both `Imported` and `Summarised` statuses.

- `BuildEmbeddingText()` concatenates the title, summary (if present), and linearised messages.
- Summarisation still exists as an optional background enrichment — if a summary is present it's included in the embedding text for potentially better quality, but it's not required.
- Auto-embedding triggers immediately after import completes, so conversations are searchable within minutes of upload.

### 2. Rewrite RAG prompt with proper chat roles and full context

`RagService.BuildMessages()` was rewritten to use proper System/User message roles via `Microsoft.Extensions.AI.ChatMessage`:

- **System message:** Frames the LLM as "Matt's AI assistant with access to his conversation history", includes up to 5 full conversation excerpts (title, date, messages — up to 4,000 chars each) as structured reference material.
- **User message:** Contains only the user's actual question.
- Full conversations are fetched from MongoDB (not just summaries) for richer context.

### 3. Increase Ollama HttpClient timeout

Added `ConfigureHttpClientDefaults` in the API service's `Program.cs` for the Ollama provider, setting `HttpClient.Timeout` to 10 minutes. This accommodates:
- Model loading time (Ollama evicts models from memory when the container's 3.8 GiB RAM is constrained).
- Slow CPU inference on large prompts, especially in Docker on ARM without GPU passthrough.

### 4. Add embedding progress to Upload UI

Extended the pipeline to track embedding state on `ImportJob`:
- Added `EmbeddingJobStatus` enum (`NotStarted`, `InProgress`, `Complete`, `Failed`) and progress counters to `ImportJob`.
- `EmbeddingService.EmbedAsync()` accepts an `IProgress<EmbeddingProgress>` callback.
- `ImportProcessingService.TryAutoEmbedAsync()` feeds progress back to the job.
- The `/conversations/status/{jobId}` endpoint now returns embedding fields.
- The Upload UI shows an "Embedding" phase with a progress bar between "Processing" and "Complete".

## Consequences

**What becomes easier:**
- Conversations are searchable via RAG immediately after import — no multi-hour summarisation wait.
- The system works reliably on CPU-only environments (Docker, CI, low-end devices) with the extended timeout.
- Users can see exactly what's happening during both import and embedding phases.
- RAG responses are dramatically better — the LLM receives structured conversation excerpts with proper role separation.

**What becomes harder or is deferred:**
- Embedding quality *may* be slightly lower without a curated summary, though in practice nomic-embed-text performs well on raw conversation text. Summaries improve things incrementally if generated later.
- The 10-minute timeout is generous — a truly stuck request will take a long time to fail. This is an acceptable trade-off for local/CPU inference.
- Model selection (using faster/better models, GPU acceleration, native Ollama vs Docker) is left as a configuration concern. The defaults (llama3.2 + nomic-embed-text in Docker) prioritise cross-platform reliability over performance.

## Alternatives Considered

**Keep summarisation as a prerequisite for embedding:** Rejected because it created an unacceptable bottleneck (~37 hours for 2,213 conversations on CPU). The quality improvement from summaries doesn't justify the delay for initial searchability.

**Reduce RAG context window (fewer results, shorter excerpts):** Partially adopted (capped at 5 results, 4,000 chars each). Could be reduced further for very constrained environments, but the current defaults produce good results with llama3.2 3B.

**Switch to a cloud LLM for chat:** The code already supports Azure OpenAI as a provider. Kept as a user configuration choice rather than a default, since local/offline operation is a project goal.

**Run Ollama natively (outside Docker) for Metal/CUDA acceleration:** Would give 5–10x speedup on Apple Silicon or NVIDIA GPUs. Left as a future enhancement / user configuration choice, since the Dockerised default works across all platforms without additional setup.
