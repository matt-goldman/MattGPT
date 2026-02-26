# 017 — Fully Analyse Export Content for Missing Import Detail

**Status:** TODO
**Sequence:** 17
**Dependencies:** 4 (ChatGPT JSON parser), 7 (store conversations in MongoDB)

## Summary

Conduct a thorough analysis of the ChatGPT export format — both the JSON structure and any rich/binary media referenced within it — to identify what valuable data the current import pipeline discards or fails to represent faithfully. Produce a written findings report and update the backlog with follow-on issues for any gaps worth addressing.

## Background

The current parser (issue 004) was built to a minimum viable spec: it extracts conversation metadata and linearises the active message thread from the `mapping` tree. Many fields in the export schema (`conversations.schema.json`) were explicitly left unused ("ignore unused fields for now"). As the ChatGPT export format has evolved — adding tool calls, code interpreter sessions, DALL·E image references, file attachments, memory entries, and other content types — the gap between what is exported and what MattGPT stores has grown.

This issue is a discovery and scoping exercise. No production code changes are required, but the findings should directly inform the next set of backlog issues.

## Requirements

1. **JSON schema audit** — Walk through `conversations.schema.json` and every field in a real export. For each field, record:
   - What it contains.
   - Whether the current parser captures it.
   - Its potential value for RAG retrieval or user-facing features.

2. **Content-type inventory** — Identify all `content_type` values that appear in real exports (e.g. `text`, `code`, `tether_quote`, `tether_browse_display`, `multimodal_text`, `execution_output`, `image_asset_pointer`, etc.) and assess how each is currently handled.

3. **Rich media assessment** — Determine what binary or external assets are referenced in the export (DALL·E-generated images, uploaded files, audio, etc.), how they are referenced (URLs, asset IDs, inline base64), and whether/how they could be stored or linked.

4. **Memory and system prompt fields** — Investigate whether the export includes ChatGPT memory entries, system prompts, or custom instructions, and assess their relevance to MattGPT's RAG use case.

5. **Metadata completeness** — Check fields like `default_model_slug`, `voice`, `conversation_template_id`, `gizmo_id`, and any others that might be useful for filtering or faceting search results.

6. **Findings document** — Write a concise findings report as a Markdown file at `docs/export-analysis.md` covering all of the above. For each identified gap, include a recommended action (fix, new issue, or ignore with rationale).

7. **Backlog updates** — For each finding that warrants a code change, create a new issue file in `docs/TODO/` and add it to the backlog in `docs/index.md`.

## Acceptance Criteria

- [ ] `docs/export-analysis.md` exists and covers all five analysis areas above.
- [ ] Every `content_type` value found in a real export is listed with its current handling status.
- [ ] Rich media references are documented, with a clear recommendation on how (or whether) to handle them.
- [ ] Any identified gaps that are worth addressing have corresponding new backlog issues.
- [ ] The findings document is self-contained enough for any contributor to act on without needing access to a raw export file.

## Notes

- A real export file is not required for the schema audit — `conversations.schema.json` in the repo root plus publicly available documentation and community research are sufficient.
- If access to a real export is available, use it to validate the schema and discover undocumented fields.
- This issue is intentionally open-ended. The goal is thoroughness, not implementation. Scope creep into actual fixes should be deferred to follow-on issues.
- Pay particular attention to `multimodal_text` parts, `image_asset_pointer` content types, and tool/function call messages — these are the areas most likely to contain valuable context that MattGPT currently misses.
