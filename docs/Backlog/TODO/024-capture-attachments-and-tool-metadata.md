# 024 — Capture File Attachments and Author/Tool Metadata

**Status:** Deferred  
**Sequence:** 24  
**Dependencies:** 4 (ChatGPT JSON parser), 7 (store conversations in MongoDB), 17 (export content analysis)  
**Deferred:** 2026-02-28 — See [ADR-006](../../Decisions/006-defer-attachment-import-until-zip-support.md). Attachment metadata has marginal standalone value; defer until whole-zip import is implemented.

## Summary

Capture file attachment metadata and tool identification fields (`author.name`, `recipient`) that are currently discarded during import. These are the most impactful metadata gaps identified in the export analysis.

## Background

The export analysis (issue 017, `docs/export-analysis.md` §4, §6) found:
- **7,999 file attachments** across 2,210 messages with filenames, MIME types, and sizes — completely invisible to the current pipeline.
- **11,719 messages** with `author.name` identifying the tool (python, dalle, browser, etc.) — not captured.
- ~~**79,910 messages** with `recipient` indicating tool dispatch targets — not captured.~~ Captured
  in issue 051.

File attachment names are particularly valuable for RAG: knowing a conversation involved `PrismCodeBlockRenderer.cs` or `Episode_36.vtt` makes it searchable by filename.

## Requirements

1. **Add `Attachment` model:**
   ```
   StoredAttachment { Id, Name, MimeType, Size, Width?, Height?, TokenSize? }
   ```
   Handle both camelCase (`mimeType`, `fileSizeTokens`) and snake_case (`mime_type`, `file_token_size`) field variants.

2. **Extend `Message` model** to capture `metadata.attachments` during deserialisation.

3. **Add fields to `StoredMessage`:**
   - `AuthorName` (string?) — from `message.author.name`
   - ~~`Recipient` (string?) — from `message.recipient`~~ **Done in issue 051**, which needed it to
     exclude assistant→tool calls from embeddings, digests and prompts.
   - `Attachments` (List<StoredAttachment>) — from `message.metadata.attachments`

4. **Include attachment filenames in embedding text.** When building embedding text in `EmbeddingService.BuildEmbeddingText()`, include attachment names (e.g. `[Attached: PrismCodeBlockRenderer.cs (text/x-csharp)]`).

5. ~~**Include tool context in embedding text.** When `author.name` is non-null (tool messages), prefix with the tool name for clarity.~~
   **Superseded by issue 051 — do not implement as written.** 051 established that `tool` and
   `system` messages must never reach embedding text, a digest, a prompt or the conversation view:
   they are 26% of all messages and dwarf the dialogue by character count. Adding them back with a
   tool-name prefix would reintroduce that defect. Capture `author.name` and `recipient` as per
   requirement 3 — they are needed to *identify* tool results — but leave embedding text to the
   dialogue only. Surfacing tool results is issue 052, which depends on this issue.

6. **Unit tests** for attachment parsing, including both camelCase and snake_case MIME type fields.

## Acceptance Criteria

- [ ] File attachments (name, MIME type, size) are stored on `StoredMessage`.
- [ ] Both `mimeType` and `mime_type` field variants are handled.
- [ ] `author.name` and `recipient` are captured on `StoredMessage`.
- [ ] Attachment filenames appear in embedding text. (Attachment *names* on user messages only — not tool message content; see requirement 5.)
- [ ] Existing tests pass; new tests for attachment parsing.

## Notes

- **Remaining scope after issue 051:** `author.name` and attachment metadata. `recipient` is done.
- **`author.name` is not needed for filtering** — issue 051 established that the author *role*
  discriminates tool results and the *recipient* discriminates tool calls; which specific tool was
  involved does not affect the decision. `author.name` is needed to **classify** tool results for
  issue 052 (is this a file-search result, python output, or a DALL·E call?), which is a cleaner
  discriminator than inferring from content type and URL presence.
- Attachment metadata (7,999 attachments: filenames, MIME types, sizes) has standalone value
  regardless of 052: it makes conversations searchable by the names of the files they involved.

- Attachment file content is not available (file IDs reference OpenAI's internal file service). Only metadata is captured.
- `video/mp2t` MIME type (140 items) is likely misidentified TypeScript `.ts` files.
