# 020 — Tool-Calling RAG Retrieval

**Status:** TODO
**Sequence:** 20
**Dependencies:** 018 (multi-turn chat), 011 (RAG pipeline)

## Summary

Evaluate and optionally implement tool/function calling so the LLM can decide *when* to search memory rather than always injecting RAG context into every prompt. This would make retrieval selective, reduce context window consumption, and open the door to other tool-based capabilities.

## Problem

The current pipeline is **RAG-first**: every user message triggers an embedding search, retrieves up to 5 conversation excerpts (~4,000 chars each), and injects them into the system message. This has several downsides:

1. **Context waste on simple queries**: "What's the time?" or "Explain Python decorators" don't need conversation history, but the pipeline still burns ~20K chars of context window on irrelevant memories.
2. **No retrieval refinement**: The LLM can't ask for *different* memories if the initial retrieval missed. It's one-shot: embed the query, get results, done.
3. **Rigid retrieval strategy**: The system always retrieves from the same store with the same parameters. Tool calling would let the LLM choose *how* to search (e.g., by topic, by date range, by project name).
4. **Limits extensibility**: Without a tool-calling pattern, adding new capabilities (web search, file lookup, calculations) requires bespoke prompt engineering for each.

## Design Considerations

### Approach A: Hybrid (Recommended for v1)

Keep lightweight RAG retrieval as the default, but add a `search_memories` tool that the LLM can invoke for deeper or more targeted searches:

- **Automatic light retrieval**: For every message, do a quick embedding search (top-2 or top-3 with a high similarity threshold). If strong matches exist, include them. This gives the "magic" of RAG without tool-calling overhead.
- **Tool for deeper search**: Register a `search_memories` function/tool that the LLM can call when it wants more context, different search terms, or filtered results (by date, topic, etc.).
- **Advantage**: Works even with models that don't support tool calling (falls back to automatic RAG). Models that do support tools get enhanced capability.

### Approach B: Tool-Only

Remove automatic RAG injection entirely. The LLM receives only the system prompt, conversation history, and tool definitions. It must explicitly call `search_memories` to get context.

- **Advantage**: No wasted context on irrelevant memories. Cleaner separation of concerns.
- **Disadvantage**: Requires a model that reliably calls tools. Small local models (llama3.2 3B) may not use tools consistently, leading to worse responses than the current always-on RAG.

### Approach C: Adaptive

Use automatic RAG when the model doesn't support tool calling; use tool-based retrieval when it does. Detect capability from model metadata or configuration.

### Tool Definitions

Potential tools to register:

| Tool | Description | Parameters |
|------|-------------|-----------|
| `search_memories` | Search past conversation history by topic/query | `query: string`, `maxResults?: int`, `minDate?: date`, `maxDate?: date` |
| `get_conversation` | Retrieve a specific past conversation by ID or title | `conversationId?: string`, `titleSearch?: string` |
| `save_note` | Save a piece of information for future recall (stretch) | `content: string`, `tags?: string[]` |

### Microsoft.Extensions.AI Tool Support

`Microsoft.Extensions.AI` supports tool/function calling via `ChatOptions.Tools` and `AIFunction`. The `IChatClient` abstraction handles tool calls automatically when configured. This aligns well with the existing architecture.

## Requirements

1. **Evaluate whether the current LLM targets (llama3.2, GPT-4o, etc.) reliably support tool/function calling.** Document findings in an ADR.
2. If tool calling is viable, implement Approach A (hybrid) as the default:
   - Reduce automatic RAG injection to a lighter touch (fewer results, higher threshold).
   - Register a `search_memories` tool via `Microsoft.Extensions.AI` function calling.
   - Handle the tool call loop (LLM calls tool → execute search → return results → LLM generates final answer).
3. Make the behaviour configurable: `RagMode = "auto" | "tools" | "hybrid"`.
4. Ensure graceful fallback when the model doesn't support tools.

## Acceptance Criteria

- [ ] An ADR documents the evaluation of tool calling across target LLM providers and the chosen approach.
- [ ] If implemented: the LLM can invoke a `search_memories` tool to retrieve relevant past conversations.
- [ ] If implemented: responses are at least as good as the current always-on RAG for queries that benefit from memory.
- [ ] Simple queries that don't need history avoid unnecessary context injection (measurable by reduced prompt size).
- [ ] The feature degrades gracefully for models that don't support function/tool calling.
- [ ] Configuration allows switching between RAG modes.

## Notes

- This issue is explicitly exploratory — the first deliverable is the ADR evaluating feasibility. Implementation follows only if the evaluation is positive.
- Tool calling adds latency (extra LLM round-trip). For slow CPU inference, this could double response time. The hybrid approach mitigates this by only using tools for deeper searches.
- Consider whether tool calling interacts with the rolling summary (issue 018) — tool results might also need to be summarised if they're large.
- The `Microsoft.Extensions.AI` `AIFunction` / `ChatOptions.Tools` API is the right abstraction layer. Avoid provider-specific tool calling APIs.
- Ollama supports tool calling for some models (Llama 3.1+, Mistral, etc.) but support varies. Test with the default model (llama3.2) before committing to this approach.
- This issue opens the door to a broader "agentic" pattern where the LLM can take actions (search, save, retrieve specific items) rather than passively receiving context.
