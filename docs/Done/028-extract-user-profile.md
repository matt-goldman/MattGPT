# 028 — Extract User Profile from Custom Instructions

**Status:** Done  
**Sequence:** 28  
**Dependencies:** 17 (export content analysis), 23 (handle non-text content types)

## Summary

Extract the user's custom instructions (`user_profile` and `user_instructions` from `user_editable_context` content type) and store them as a standalone user profile document. This profile can be used as system context for MattGPT's own chat sessions.

## Background

The export analysis (issue 017, `docs/export-analysis.md` §5) found **1,170 `user_editable_context` messages** across conversations. These contain:
- `user_profile` — the "About me" section (e.g. personal background, profession, preferences)
- `user_instructions` — the "How should ChatGPT respond" section (e.g. response style, verbosity)

These are identical across most conversations (the same custom instructions are injected each time) but may change over time as the user updates them.

## Requirements

1. **During import**, detect `user_editable_context` messages and extract the latest version of `user_profile` and `user_instructions` (latest by `create_time`).

2. **Store as a user profile document** in MongoDB — a single document (or a small history) rather than per-conversation.

3. **Optionally use as system context** — provide the extracted user profile to `ChatSessionService` so MattGPT can personalise responses using the same instructions the user set in ChatGPT.

4. **Do not embed per-conversation** — the custom instructions should not be included in per-conversation embedding text (they're the same everywhere and would waste embedding context).

## Acceptance Criteria

- [x] `user_editable_context` messages are detected during import.
- [x] The latest `user_profile` and `user_instructions` are stored as a user profile document.
- [x] The user profile is available for use as system context in chat sessions.
- [x] Custom instructions are not included in per-conversation embedding text.

## Notes

- This is a quality-of-life enhancement rather than a critical gap.
- Consider showing the extracted profile on the UI settings page so the user can review/edit it.
- If the user updates their custom instructions between exports, the import should capture the changes (compare timestamps).
