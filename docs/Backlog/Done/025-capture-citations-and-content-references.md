# 025 — Capture Citations and Content References

**Status:** Done  
**Sequence:** 25  
**Dependencies:** 4 (ChatGPT JSON parser), 7 (store conversations in MongoDB), 17 (export content analysis)

## Summary

Parse and store citation and content reference data from message metadata. These provide precise source attribution that is valuable for RAG retrieval quality and could power source-linked results in the UI.

## Background

The export analysis (issue 017, `docs/export-analysis.md` §2) found:
- **8,651 citations** across 24,588 messages — file and web source references with character-level positions.
- **13,106 content references** across 22,551 messages — rich typed references including files, web pages, images, entities, and more.

Citation format types: `tether_og` (448), `berry_file_search` (144), `tether_v4` (102).

Content reference types (top 5): `hidden` (6,416), `attribution` (2,406), `grouped_webpages` (2,388), `sources_footnote` (583), `file` (441).

## Requirements

1. **Add `StoredCitation` model:**
   ```
   StoredCitation { StartIndex, EndIndex, FormatType, Type?, Name?, Source?, Text? }
   ```

2. **Add `StoredContentReference` model:**
   ```
   StoredContentReference { Type, Name?, MatchedText?, Snippet?, Url?, Source? }
   ```

3. **Extend `Message` model** to capture `metadata.citations` and `metadata.content_references`.

4. **Add fields to `StoredMessage`:**
   - `Citations` (List<StoredCitation>?)
   - `ContentReferences` (List<StoredContentReference>?)

5. **Include citation context in embedding text** — when citations reference files, include the file name; when they reference web sources, include the URL/title.

6. **Unit tests** for citation and content reference parsing.

## Acceptance Criteria

- [x] Citations are parsed from `metadata.citations[]` and stored on `StoredMessage`.
- [x] Content references are parsed from `metadata.content_references[]` and stored on `StoredMessage`.
- [x] File and web citations contribute to embedding text.
- [x] Existing tests pass; new tests for citation parsing.

## Notes

- Content references with type `hidden` (6,416) are internally used and may not need to be stored — evaluate during implementation.
- This issue has synergy with the existing clickable citation links feature (issue 015) — the stored citations could also enrich MattGPT's own RAG responses.
