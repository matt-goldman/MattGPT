# 052 — Surface Tool Results: Files Tab and an On-Demand LLM Tool

**Status:** TODO
**Sequence:** 52
**Type:** Feature
**Dependencies:** 024 (capture attachments and author/tool metadata) — **hard blocker**; 051 (tool-result filtering); 030 (search_memories tool)

---

## Summary

Issue 051 stopped tool results from polluting embeddings, digests, prompts and the conversation
view — but it deliberately **kept them in the store**. This issue makes them useful again, on
purpose and on demand, in two ways:

1. **For the user** — a Files/Sources view on a conversation, listing what was uploaded, fetched or
   produced, viewable and exportable.
2. **For the model** — a tool the LLM can call to inspect a specific tool result *when it needs to*,
   rather than having it all forced into context.

This is a large amount of genuinely valuable content that is currently inert: in the reference
export, `file_search` (5,909 messages) and `myfiles_browser` (1,840) carry the text of files
uploaded to conversations and projects, plus 7,999 attachment records and 5,161 `tether_quote` /
1,244 `tether_browsing_display` web results.

## Why 024 Blocks This

Tool results cannot currently be attributed. `StoredMessage` keeps `Role` but **not**
`author.name` (which tool produced the result) or `recipient` (which tool was called). Both are
parsed into the `Message` model but dropped in `StoredMessage.From`, and `recipient` is not in the
model at all. Without them, a `role: tool` message cannot be classified as a file-search result
versus a Python output versus a DALL·E call. **024 must land first.**

Note that 024's requirement 5 ("include tool context in embedding text") conflicts with 051 and
must not be implemented as written — capture the metadata, but do not feed tool messages into
embedding text.

## Phasing

### Phase 1 — `file_search` / `myfiles_browser` only

The highest-value, best-defined slice: files the user actually uploaded.

- Classify tool messages by `author.name` into a `ToolResultKind` (start with `FileSearch`, and an
  `Other` fallback so nothing is lost).
- Group the file-retrieval results of a conversation into a distinct set of referenced files,
  joined with the `StoredAttachment` metadata from 024 (filename, MIME type, size) and the
  existing `StoredCitation` / `StoredContentReference` data, which already carry cited file names.
- De-duplicate: the same file is typically quoted many times across a conversation, once per
  retrieved chunk. The view wants one entry per file, with its chunks underneath.

### Phase 2 and beyond — for consideration, not committed

Treat each as its own decision, in rough order of likely value:

| Source | `author.name` | Notes |
|---|---|---|
| Web fetches | `web.run`, `browser`, `web` | Already partly captured as `tether_quote` / citations; overlaps with existing citation handling |
| Python / code execution | `python`, `container.exec` | Output already parsed as `execution_output` / `citable_code_output` |
| Canvas documents | `canmore.*` | Textdoc create/update/comment — may reconstruct document versions |
| Memory writes | `bio` | What ChatGPT chose to remember; interesting for a user profile, and 027 already covers user-profile extraction — check for overlap |
| Image generation | `dalle.text2im` | Prompt is available; the image itself is not in the export |
| Connectors / plugins | `t2uay3k.sj1i4kz`, `diagrams_*`, `api_tool.*`, etc. | Long tail, ~40 distinct names, mostly low volume |

## Surface 1 — The UI

- A Files (or Sources) tab/button on the conversation view, shown only when the conversation has
  tool results. Reuse LumexUI components; see `src/UI/MattGPT.Web/llms.txt`.
- List each referenced file: name, type, size where known, and how many times it was drawn on.
- Allow viewing the retrieved text, and exporting it — fitting alongside 048's existing on-demand
  Markdown record export.
- The data is already reachable: `GET /conversations/{id}?includeHidden=true` returns every stored
  message. A dedicated projection endpoint (e.g. `GET /conversations/{id}/tool-results`) is likely
  cleaner than making the UI classify raw messages.

## Surface 2 — The LLM Tool

A tool alongside the existing `search_memories` / `search_memories_keyword`
(`SearchMemoriesTool`), letting the model inspect tool results for a conversation it has already
retrieved.

**This must be carefully managed.** The whole point of 051 was to keep this content out of context
by default; a tool that dumps every file when a conversation is loaded would reintroduce the defect
through a different door.

Design constraints:

1. **Never automatic.** Nothing about retrieving a conversation should pull in its files. The tool
   is offered, not applied.
2. **Two-step, cheap first.** A listing call (`list_conversation_files(conversationId)`) returning
   just names/types/sizes, then a fetch call (`get_conversation_file(conversationId, fileName)`)
   for the content. The model should be able to decide relevance from the cheap call.
3. **Budgeted.** Paginate or truncate file content against an explicit character budget, consistent
   with `DefaultExcerptChars` and the existing RAG budget handling. A single file can exceed the
   entire context window.
4. **Explicit guidance in the tool description and system prompt** on *when* to call it — namely
   when either:
   - the user is interactively asking about something that is, or is likely to be, in a file; or
   - the retrieved conversation context indicates the answer or pertinent information is in the
     file(s) — e.g. the dialogue refers to "the spreadsheet" or "that document", or a citation
     points at a file.

   And when **not** to: general questions answerable from the dialogue and digest alone.
5. **Observable.** Log when the tool fires and how many characters it returned, so its effect on
   token usage can actually be measured.

## Open Questions

- Should file content be embedded separately — its own vectors, its own collection — so files are
  retrievable by similarity rather than only by name? This is a much larger change (it is close to
  a second RAG corpus) and should probably be its own issue, informed by how the tool performs.
- Does the `bio` tool data overlap enough with issue 027 (user profile extraction) that they should
  be done together?
- Is a separate store warranted, or do read-time projections over `LinearisedMessages` remain
  sufficient? Start with projections; revisit only if the queries prove awkward or slow.

## Acceptance Criteria

- [ ] 024 has landed: `author.name`, `recipient` and attachment metadata are on `StoredMessage`.
- [ ] Tool results are classified by kind, with `file_search` / `myfiles_browser` identified and an
      `Other` fallback that loses nothing.
- [ ] An endpoint projects a conversation's referenced files, de-duplicated, with metadata.
- [ ] The conversation view has a Files tab listing them, viewable and exportable, shown only when
      the conversation has tool results.
- [ ] An LLM tool exposes a cheap listing call and a separate, budgeted content-fetch call.
- [ ] The tool is never invoked automatically on retrieval, and its description plus the system
      prompt state when to use it and when not to.
- [ ] Tool invocations and returned sizes are logged.
- [ ] Embeddings, digests and the default prompt path are unchanged — 051's filtering still holds,
      verified by test.
- [ ] Phase 2 sources remain explicitly out of scope and recorded as such.
