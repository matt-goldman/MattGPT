# 051 — Defect: Tool Results Pollute Embeddings, Digests, Prompts and the Conversation View

**Status:** Done
**Sequence:** 51
**Type:** Defect (against 004, 009, 022)
**Dependencies:** 004 (ChatGPT JSON parser), 009 (embeddings), 017 (export content analysis), 022 (summarisation)

---

## Summary

A ChatGPT export interleaves the human/assistant dialogue with **tool traffic**: the full text of
every file uploaded to a conversation or project (`file_search`, `myfiles_browser`), every web
fetch (`browser`, `web.run`), every Python execution output, plus image generation, canvas edits
and the system scaffolding that drives all of it. Nothing in the pipeline filtered on the author
role, so this traffic was embedded, summarised, sent to the model as retrieved memory, returned as
search snippets, and rendered in the conversation view.

It is not a marginal amount of content. In the reference export
(`docs/TechnicalReference/export-analysis.md`) **11,719 of 79,910 messages have `author.role == "tool"`
and a further 9,035 are `system`** — 26% of all messages, and far more than 26% by character count,
because a single `file_search` result can be an entire document. `file_search` alone accounts for
5,909 messages and `myfiles_browser` another 1,840.

This is a plausible root cause of several previously separate symptoms: context-window blowouts,
poor retrieval relevance, inaccurate answers, and a conversation view that looked corrupted.

## Root Cause

The role was carried through to `StoredMessage.Role` as a plain `string` with no validation and no
filtering. Four read paths filtered messages with the same hand-copied predicate
`!m.IsHidden && m.Weight != 0.0`, and a fifth filtered nothing at all.

**That predicate does not exclude tool traffic.** Only **7,884** of 79,910 messages carry
`weight: 0.0`, while there are **20,754** `tool` + `system` messages. Most tool messages carry
`weight: 1.0` and no `is_visually_hidden_from_conversation` flag, so they passed the filter
untouched. The user-visible proof: the conversation endpoint already applied this filter, and the
conversation view was still unreadable.

The worst offender was `StoredConversationExtensions.ToExcerpt()`, which applied **no filter of any
kind** — not even weight/hidden. It is the method that renders a retrieved conversation into the
model's prompt (`RagService.BuildMessages`) and into `search_memories` tool results
(`SearchMemoriesTool`). So the single path with the most direct effect on answer quality and token
usage was the one with no filtering, and it disagreed with what had been indexed: a conversation
was embedded on its dialogue, then handed to the model as dialogue *plus* every file and web page
it had ever touched.

Duplicating the predicate across five sites is what allowed the fifth to drift unnoticed.

## Fix

Tool results are **deliberately retained** in `StoredConversation.LinearisedMessages`. They are the
only record of which files and sources a conversation involved, and issue 052 builds on them.
Filtering happens on **read**, not on import, so nothing is lost.

1. **Single shared definition.** New `StoredMessageFilters` (`src/Common/Contracts/Models/`) defines
   `StoredMessage.IsConversational` (`role` is `user` or `assistant`, not hidden, non-zero weight)
   and `StoredConversation.ConversationalMessages`. Implemented as extension members so the
   serialised shape of `StoredMessage` is unchanged — important because the enum/JSONB
   serialisation contract on these documents is load-bearing for the Postgres backend.
2. **All five read paths now use it**, replacing the hand-copied predicate:
   - `ToEmbeddingText` — what gets embedded
   - `ToExcerpt` — what reaches the model (previously unfiltered)
   - `SummarisationService.BuildTranscript` — digests
   - `GET /conversations/{id}` — the conversation view
   - `ConversationTextSnippets.Build` — keyword-search snippets
3. **Escape hatch preserved.** `GET /conversations/{id}?includeHidden=true` still returns every
   stored message, which is how 052 will reach tool results.

## Residual Gaps (deliberately not fixed here)

- **Assistant tool *calls* are still included.** A message with `role: assistant` and
  `recipient: python` (1,020 messages have `recipient: assistant`, 824 `python`, 602 `bio`) is
  scaffolding, not dialogue, but `recipient` is not captured by the parser at all. Capturing it is
  issue **024**, which must land before these can be filtered.
- **Reasoning traces** (`thoughts`, 1,042; `reasoning_recap`, 788) are `assistant`-role and remain
  included. Arguably they should not be embedded; deferred as a separate judgement call.
- **Issue 024 requirement 5 conflicts with this fix** — it says to "include tool context in
  embedding text" by prefixing tool messages with `author.name`. That would reintroduce the defect.
  024 has been annotated accordingly: capture `author.name`, but do not add tool messages to
  embedding text.

## Operational Note

**Re-embedding is required.** This changes `ToEmbeddingText` output, so existing vectors were built
from polluted text and will not match on the same terms until rebuilt. Run `POST /conversations/embed`.
Digests generated before this fix also summarised tool output and should be regenerated.

## Acceptance Criteria

- [x] `tool` and `system` messages are excluded from embedding text.
- [x] `tool` and `system` messages are excluded from digests/summaries.
- [x] `tool` and `system` messages are excluded from anything sent to a model (`ToExcerpt`, used by
      the RAG prompt and the `search_memories` tool).
- [x] `tool` and `system` messages are excluded from keyword-search snippets.
- [x] `tool` and `system` messages are excluded from the conversation view by default, and remain
      reachable via `?includeHidden=true`.
- [x] Tool results are still stored and not dropped on import.
- [x] The filter is defined in exactly one place, so the five paths cannot drift again.
- [x] Tests cover the role filter, each of the five paths, and the retention of tool messages.
- [x] All tests pass (`dotnet test`: 358 passed, 0 failed).

## Files Changed

- `src/Common/Contracts/Models/StoredMessageFilters.cs` (new)
- `src/API/MattGPT.ApiService/Extensions/StoredConversationExtensions.cs`
- `src/API/MattGPT.ApiService/Services/SummarisationService.cs`
- `src/API/MattGPT.ApiService/Endpoints/ConversationsEndpoints.cs`
- `src/Common/Contracts/Models/ConversationTextSearchResult.cs`
- `tests/MattGPT.ApiService.Tests/ToolTrafficFilterTests.cs` (new, 13 tests)
