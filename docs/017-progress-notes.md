# 017 — Export Content Analysis: Progress Notes

## 2026-02-27

### Phase 1: Data Collection (Complete)
- Created `tools/analyse-export.py` — streaming analysis using ijson
- Processed 8 export files (9–141 MB), 2913 conversations, 79,910 messages
- Raw statistics saved to `docs/export-analysis-raw.md`
- Committed: `a4bf2ec`

### Phase 2: Findings Document (In Progress)
- Writing `docs/export-analysis.md` covering all 5 analysis areas from the issue
- Cross-referencing raw data with current parser/model code
- Identifying gaps between schema and what MattGPT captures

### Key observations so far
- 12 distinct content_type values found; parser only handles `text` parts — all others are passed through as raw JSON strings
- 1,378 image asset pointers (DALL-E, uploads, screenshots) — completely invisible to RAG
- 7,999 file attachments with rich MIME type diversity — not captured at all
- 8,651 citations and 13,106 content references — lost during import
- 1,170 user_editable_context messages (custom instructions) — included in linearised thread but not separately tracked
- tool/code/browse content types carry significant context that's flattened to raw JSON in Parts
- 854 canvas documents, 788 reasoning recaps, 1,042 thoughts blocks — all discarded or opaque
