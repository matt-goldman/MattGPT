# ADR-005: Tool-Calling RAG Retrieval — Feasibility Evaluation

**Date:** 2026-02-27
**Status:** Accepted
**Related Issues:** [020-tool-calling-rag-retrieval.md](../TODO/020-tool-calling-rag-retrieval.md)

### Context

The current RAG pipeline is **always-on**: every user message triggers an embedding search against Qdrant (top-5, min score 0.5), fetches full conversation excerpts from MongoDB (up to 4,000 chars each), and injects them into the system prompt. This consumes ~20K characters of context window on every turn regardless of whether the query benefits from memory.

Issue 020 asks us to evaluate whether the LLM can decide **when** to search memory via tool/function calling, rather than always injecting context. This would reduce context waste, allow retrieval refinement (the LLM can re-search with different terms), and establish a pattern for future tool-based capabilities.

This ADR evaluates tool-calling feasibility across MattGPT's target LLM providers and recommends an implementation approach.

### Evaluation of Tool-Calling Support

#### Microsoft.Extensions.AI (M.E.AI 10.3.0)

The abstraction layer already supports tool calling:

- `ChatOptions.Tools` accepts a list of `AITool` instances (including `AIFunction`).
- `AIFunctionFactory.Create()` builds `AIFunction` objects from .NET delegates or methods with automatic JSON schema generation for parameters.
- `IChatClient.GetResponseAsync()` and `GetStreamingResponseAsync()` handle the tool-call loop automatically when `ChatOptions.ToolMode` is set — the client calls the LLM, detects tool-call requests in the response, invokes the registered functions, feeds results back, and returns the final answer.
- `FunctionInvocationOptions` on `ChatOptions` control whether tool invocations happen automatically or require manual orchestration.

This means MattGPT does not need provider-specific tool-calling code. The existing `IChatClient` injection works as-is; we only need to pass `ChatOptions.Tools` when calling `GetResponseAsync` / `GetStreamingResponseAsync`.

#### Provider-Specific Findings

| Provider | Tool Calling Support | Notes |
|----------|---------------------|-------|
| **Ollama** (via OllamaSharp / CommunityToolkit.Aspire) | Supported since Ollama 0.3.0. OllamaSharp maps `ChatOptions.Tools` to Ollama's native tool-call API. | Model-dependent — the model must have been trained for tool use. |
| **FoundryLocal** (OpenAI-compatible) | Supported via `Microsoft.Extensions.AI.OpenAI`. Uses standard OpenAI function-calling protocol. | Model-dependent — the served model must support function calling. |
| **Azure OpenAI** | Full support via `Azure.AI.OpenAI` + `Microsoft.Extensions.AI.OpenAI`. GPT-4o, GPT-4 Turbo, GPT-3.5 Turbo all support function calling. | Most reliable provider for tool calling. |

#### Model-Specific Findings

| Model | Tool Calling | Quality Assessment |
|-------|-------------|-------------------|
| **llama3.2 3B** (current default) | Technically supported by Ollama, but **unreliable**. The 3B parameter model frequently ignores tool definitions, hallucinates tool names, or produces malformed JSON arguments. | **Not recommended** as a tool-calling model. Fine for text generation. |
| **llama3.1 8B+** | Good tool-calling support. Was specifically trained with tool-use capabilities. Ollama officially lists it as tool-capable. | Viable but requires more RAM (~5-6 GB) and is slower on CPU. |
| **llama3.3 70B** | Excellent tool-calling support. | Too large for typical local inference. |
| **mistral 7B / mixtral** | Solid tool-calling support in Ollama. | Viable alternative to llama3.1 8B. |
| **GPT-4o / GPT-4 Turbo** (Azure OpenAI) | Excellent, industry-leading tool-calling reliability. | Requires cloud access and API key. |
| **qwen2.5 7B** | Good tool-calling support, often cited as one of the best small models for tools. | Viable alternative. |

**Key finding:** The current default model (llama3.2 3B) is not reliable enough for tool calling. A tool-based approach requires either upgrading the default chat model or making tool calling conditional on model capability.

### Decision

**Adopt Approach A (Hybrid)** from the issue specification, with the following design:

#### 1. Keep automatic lightweight RAG as the baseline

Every message continues to trigger embedding search, but with **reduced parameters** when tool calling is active:

- Lower `TopK` (e.g., 2 instead of 5) and higher `MinScore` threshold (e.g., 0.65 instead of 0.5).
- Only include results that are strong matches — a "light touch" that catches obvious relevant memories without flooding the context.
- When tool calling is disabled or unsupported, fall back to the current full RAG injection (top-5, min score 0.5).

#### 2. Register a `search_memories` tool for deeper/targeted retrieval

Define an `AIFunction` via `AIFunctionFactory.Create()` that the LLM can invoke:

```
search_memories(query: string, maxResults?: int)
```

- Executes the same embedding → Qdrant search → MongoDB fetch pipeline as the current `RagService`, but with the LLM-chosen query string.
- Returns formatted conversation excerpts that get fed back into the LLM context as tool-call results.
- The LLM can call this multiple times with different queries in one turn if needed (multi-step retrieval).

#### 3. Three-mode configuration via `RagOptions.Mode`

| Mode | Behaviour | When to use |
|------|-----------|-------------|
| `auto` (default) | Full automatic RAG injection on every message. Current behaviour. No tools registered. | Models that don't support tool calling (llama3.2 3B). |
| `hybrid` | Light auto-RAG (top-2, higher threshold) + `search_memories` tool registered. | Models with reliable tool calling (llama3.1 8B+, GPT-4o). |
| `tools` | No automatic RAG injection. `search_memories` tool only. LLM must explicitly search. | High-capability models (GPT-4o) where users want minimal context waste. |

The mode is set via configuration (`RAG:Mode`). This is deliberately a static configuration setting rather than auto-detection in v1 — auto-detecting model capabilities is fragile and would require maintaining a model capability registry.

#### 4. Graceful degradation

- If `Mode` is `hybrid` or `tools` but the LLM never calls the tool (because the model is too small or confused), the `hybrid` mode's light auto-RAG still provides some context. In `tools` mode, the response simply won't have memory context — acceptable as a conscious user configuration choice.
- If the tool call fails (Qdrant down, embedding error), the tool returns an error message to the LLM which can then respond without memory context.
- The streaming response path needs to handle the tool-call round-trip. M.E.AI's `GetStreamingResponseAsync` handles this when automatic function invocation is enabled, but the extra round-trip adds latency (~2–5s on CPU for the tool-call response generation).

#### 5. Implementation approach for the tool-call loop

M.E.AI's `IChatClient` supports automatic function invocation via `ChatOptions`:

```csharp
var options = new ChatOptions
{
    Tools = [AIFunctionFactory.Create(SearchMemories, "search_memories", "Search past conversation history by topic or query")],
    ToolMode = ChatToolMode.Auto,
};
// GetResponseAsync / GetStreamingResponseAsync handle the loop automatically
```

For streaming, M.E.AI's `FunctionInvocingChatClient` middleware (registered via `UseFunctionInvocation()` in the client pipeline) intercepts tool-call chunks, invokes the function, and feeds results back before continuing to stream. This is the recommended approach and avoids manual tool-call loop management.

Alternatively, for more control (e.g., to inject tool results into the session history), we can set `AutoInvoke = false` and handle the loop manually in `RagService`.

### Consequences

**Easier:**

- Simple queries ("What is a monad?", "What time is it?") avoid 20K chars of irrelevant conversation history in the prompt, leading to faster responses and better answers from small models.
- The LLM can perform targeted searches with its own query formulation, potentially finding better matches than the user's raw question as an embedding query.
- Establishes a tool-calling pattern that can be extended with additional tools (`get_conversation`, `save_note`, web search, etc.) without architectural changes.
- The three-mode configuration lets users choose the right trade-off for their model and hardware.

**Harder:**

- Tool-calling adds an extra LLM round-trip (the model generates a tool call, we execute it, then the model generates the final response). On CPU inference this could add 5–15 seconds of latency per tool call.
- The hybrid approach has two code paths (auto-RAG parameters differ by mode), adding some complexity to `RagService`.
- Users must know their model's capabilities to choose the right mode. Misconfiguring (e.g., `tools` mode with llama3.2 3B) will produce poor results. Documentation and sensible defaults (`auto`) mitigate this.
- Streaming with tool calls is more complex — the stream pauses during tool execution, which the UI needs to handle gracefully (e.g., a "searching memories..." indicator).

### Alternatives Considered

| Alternative | Reason rejected |
|-------------|----------------|
| **Tool-only (Approach B)** | Too dependent on model quality. The default model (llama3.2 3B) can't reliably call tools, so out-of-the-box experience would regress. Kept as the `tools` configuration mode for capable models. |
| **Adaptive auto-detection (Approach C)** | Detecting tool-calling capability at runtime is unreliable — there's no standard API for querying model capabilities. Would need a hardcoded model→capability mapping that goes stale. Rejected in favour of explicit configuration. |
| **Semantic Router / classifier** | Use a small classifier to decide whether a query needs RAG before calling the LLM. Adds complexity and another model dependency. The hybrid approach achieves a similar result more simply by letting the LLM itself decide via tool calling. |
| **Always-on RAG with reduced context** | Simply reduce TopK and excerpt size for all queries. Doesn't solve the fundamental problem — irrelevant results still waste context and can confuse the model. Partial mitigation already exists (MinScore threshold). |
| **Wait for llama3.2 to improve** | Tool-calling quality in small models is improving rapidly, but we can't block on external model releases. The hybrid approach works today and will automatically benefit from better models. |
