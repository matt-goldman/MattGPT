# 023 — Handle Non-Text Content Types in Parser

**Status:** TODO  
**Sequence:** 23  
**Dependencies:** 4 (ChatGPT JSON parser), 7 (store conversations in MongoDB), 17 (export content analysis)

## Summary

Update `StoredMessage.From()` and the `Content` model to correctly extract text from all content types, not just those that use `parts`. Currently, content types like `code`, `tether_quote`, `tether_browsing_display`, `execution_output`, `thoughts`, and `reasoning_recap` store their text in fields other than `parts`, causing their content to be lost during import.

## Background

The export analysis (issue 017, `docs/export-analysis.md` §3) found that 6 of 12 content types use fields other than `parts` for their primary text content. This affects ~11,000 messages (14% of total). The current `StoredMessage.From()` method reads only `Content.Parts`, so these messages end up with empty `Parts` lists.

## Requirements

1. **Extend the `Content` model** (`ChatGptExport.cs`) to deserialise additional fields:
   - `language` (for `code`)
   - `result`, `summary` (for `tether_browsing_display`)
   - `url`, `domain`, `title` (for `tether_quote`)
   - `thoughts` (for `thoughts`)
   - `content` (for `reasoning_recap`)
   - `user_profile`, `user_instructions` (for `user_editable_context`)
   - `output_str` (for `citable_code_output`)

2. **Update `StoredMessage.From()`** to extract text content based on `content_type`:
   - `text` / `multimodal_text` — use `parts` (current behaviour)
   - `code` — use `text` field; store `language` as metadata
   - `execution_output` — use `text` field
   - `tether_quote` — use `text`; also store `url`, `domain`, `title` in a structured field
   - `tether_browsing_display` — use `result` and/or `summary`
   - `thoughts` — extract `thoughts[].content` array
   - `reasoning_recap` — use `content` field
   - `user_editable_context` — use `user_profile` + `user_instructions`
   - `system_error` — use `text` + `name`
   - `citable_code_output` — use `output_str`
   - `computer_output` — extract `state.title` + `state.url` if present

3. **Handle image_asset_pointer in parts** — when a `parts` entry is an `image_asset_pointer` object, generate a human-readable placeholder (e.g. `[Image: 1200×1600, DALL-E generated]` or `[Uploaded image: 800×600]`) instead of raw JSON.

4. **Preserve backward compatibility** — existing MongoDB documents should not need migration. New fields should be nullable/optional.

5. **Unit tests** — add tests for each content type in `ConversationParserTests.cs` or a new `StoredMessageTests.cs`.

## Acceptance Criteria

- [ ] All 12 content types produce meaningful text in `StoredMessage.Parts` (or equivalent field).
- [ ] `image_asset_pointer` objects in parts produce descriptive placeholders, not raw JSON.
- [ ] `tether_quote` messages retain URL, domain, and title information.
- [ ] `code` messages retain the code text and language.
- [ ] `tether_browsing_display` messages include browse result/summary text.
- [ ] Existing tests continue to pass.
- [ ] New unit tests cover each content type.

## Notes

- This is the single highest-impact improvement identified in the export analysis.
- The changes are concentrated in `StoredMessage.From()` and `ChatGptExport.cs` — minimal blast radius.
- Consider adding a `TextContent` computed property on `StoredMessage` that normalises all content types to a single readable string for downstream use by the embedder/summariser.
