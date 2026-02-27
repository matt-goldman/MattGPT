# 024 — Capture File Attachments and Author/Tool Metadata

**Status:** Deferred  
**Sequence:** 24  
**Dependencies:** 4 (ChatGPT JSON parser), 7 (store conversations in MongoDB), 17 (export content analysis)  
**Deferred:** 2026-02-28 — See [ADR-006](../Decisions/006-defer-attachment-import-until-zip-support.md). Attachment metadata has marginal standalone value; defer until whole-zip import is implemented.

## Summary

Capture file attachment metadata and tool identification fields (`author.name`, `recipient`) that are currently discarded during import. These are the most impactful metadata gaps identified in the export analysis.

## Background

The export analysis (issue 017, `docs/export-analysis.md` §4, §6) found:
- **7,999 file attachments** across 2,210 messages with filenames, MIME types, and sizes — completely invisible to the current pipeline.
- **11,719 messages** with `author.name` identifying the tool (python, dalle, browser, etc.) — not captured.
- **79,910 messages** with `recipient` indicating tool dispatch targets — not captured.

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
   - `Recipient` (string?) — from `message.recipient`
   - `Attachments` (List<StoredAttachment>) — from `message.metadata.attachments`

4. **Include attachment filenames in embedding text.** When building embedding text in `EmbeddingService.BuildEmbeddingText()`, include attachment names (e.g. `[Attached: PrismCodeBlockRenderer.cs (text/x-csharp)]`).

5. **Include tool context in embedding text.** When `author.name` is non-null (tool messages), prefix with the tool name for clarity.

6. **Unit tests** for attachment parsing, including both camelCase and snake_case MIME type fields.

## Acceptance Criteria

- [ ] File attachments (name, MIME type, size) are stored on `StoredMessage`.
- [ ] Both `mimeType` and `mime_type` field variants are handled.
- [ ] `author.name` and `recipient` are captured on `StoredMessage`.
- [ ] Attachment filenames appear in embedding text.
- [ ] Existing tests pass; new tests for attachment parsing.

## Notes

- Attachment file content is not available (file IDs reference OpenAI's internal file service). Only metadata is captured.
- `video/mp2t` MIME type (140 items) is likely misidentified TypeScript `.ts` files.
