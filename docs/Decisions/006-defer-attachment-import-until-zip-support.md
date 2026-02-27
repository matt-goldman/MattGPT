# ADR-006: Defer Attachment & Tool Metadata Import Until Whole-Zip Import

**Date:** 2026-02-28  
**Status:** Accepted  
**Related Issues:** [024-capture-attachments-and-tool-metadata.md](../TODO/024-capture-attachments-and-tool-metadata.md)

### Context

Issue 024 proposed capturing file attachment metadata (filenames, MIME types, sizes) and tool identification fields (`author.name`, `recipient`) from the ChatGPT export. The export analysis found 7,999 file attachments across 2,210 messages and 11,719 messages with `author.name` identifying the tool that produced them.

An investigation of the actual ChatGPT export zip contents revealed:

| Category | In JSON metadata | Files on disk | Coverage |
|----------|----------------:|-------------:|--------:|
| Images (png/jpeg/gif) | 949 | 600 | 63% |
| Code/text (C#, MD, JS, TS, etc.) | 3,686 | 1 | ~0% |
| DALL-E generations | — | 124 | (separate folder) |
| User profile images | — | 109 | (separate folder) |
| **Total** | **4,635** | **~834** | **~18%** |

Key findings:

1. **Code and text file attachments are NOT included in the export.** Only images are present on disk. The 3,685 uploaded code files, PDFs, spreadsheets, and other documents exist only as metadata references to OpenAI's internal file service.

2. **Image files on disk use a predictable naming convention** (`file-{id}-{suffix}.{ext}`) that maps directly to the attachment `id` in JSON metadata. Auto-import from the zip would be straightforward — no filename disambiguation needed.

3. **Attachment metadata alone has marginal RAG value.** While filenames like `PrismCodeBlockRenderer.cs` in embedding text would make conversations more findable, the conversation text already discusses uploaded files in detail. The incremental search improvement doesn't justify the implementation cost in isolation.

4. **`author.name` and `recipient` have limited standalone value.** Issue 023 already implemented content-type-aware extraction, so tool messages are already distinguishable by their content type (`code`, `execution_output`, `tether_browsing_display`, etc.). The additional granularity of knowing it was specifically `python` vs `dalle` vs `browser` is marginal for RAG quality.

5. **The current import flow accepts individual JSON files.** Supporting attachment import properly requires whole-zip import so that file references in JSON can be resolved against files in the same archive — a different UX paradigm from the current multi-file JSON upload.

### Decision

Defer issue 024 (Capture File Attachments and Author/Tool Metadata) until a whole-zip import feature is implemented. At that point:

- Parse the zip structure to identify conversation JSON files and media assets.
- Auto-resolve attachment metadata against files present in the archive.
- Store images in blob storage (Aspire makes adding Azure Blob or local Azurite trivial).
- Rewrite `file-service://` URIs to blob storage URIs on import.
- Report to the user which attachments were resolved vs. metadata-only.
- Capture `author.name` and `recipient` as part of the same effort, since they're most valuable when paired with rich attachment context.

### Consequences

**What becomes easier:**
- The backlog can prioritise higher-value items (conversation-level metadata, citations, user profile extraction) that improve RAG quality more directly.
- When zip import is eventually built, attachment handling will be designed holistically rather than as a metadata-only half-measure.

**What becomes harder:**
- Conversations involving uploaded files remain searchable only by their text content, not by attachment filename. This is an acceptable trade-off since the conversation text typically describes the files in detail.
- Tool identification is slightly less precise (content type only, not specific tool name). Acceptable since content types already distinguish the major categories.

### Alternatives Considered

1. **Implement metadata-only capture now (024 as written).** Store filenames, MIME types, and sizes on `StoredMessage`; include filenames in embedding text; capture `author.name`/`recipient`. Rejected because the value is marginal without the actual file content, and `author.name` adds little over existing content-type distinctions.

2. **Implement metadata capture now + user upload flow for missing files.** After import, report missing attachments and let users upload them individually. Rejected because: (a) users are unlikely to have the original files organised for re-upload, (b) filename collision risk across conversations requires complex disambiguation UI, (c) the file IDs in the export don't match anything the user would recognise.

3. **Implement zip import now.** Rejected as premature — the current multi-file JSON upload flow works well and the zip feature is a larger UX change that should be designed intentionally.
