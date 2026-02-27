# 016 — Support Importing Multiple Files

**Status:** Done
**Sequence:** 16
**Dependencies:** 5 (file upload UI), 6 (background processing service)

## Summary

ChatGPT exports are now split across multiple JSON files instead of a single `conversations.json`. Allow users to select and upload multiple files in one operation so the entire export can be imported without repeating the upload flow for each file.

## Background

The current upload UI (`/upload`) accepts a single `.json` file at a time. Recent ChatGPT data exports from OpenAI are split into several JSON files (e.g. `conversations-1.json`, `conversations-2.json`, etc.). Importing them one by one is laborious and error-prone — the user must repeat the upload for every file and manually track which files they have already processed.

## Requirements

1. **Multi-file selection** — Update the file picker on the `/upload` page to allow selecting multiple `.json` files in a single file-chooser dialog.
2. **Batch upload** — Upload and process all selected files, either:
   - Sequentially (one file at a time, sharing a single progress indicator), or
   - In parallel (separate progress per file), whichever is simpler to implement reliably.
3. **Per-file progress** — Show progress and status for each file individually so the user can see which files have been processed and which have failed.
4. **Duplicate handling** — If a conversation that already exists in MongoDB is encountered during a subsequent file's import, it should be skipped (upsert / idempotent import) rather than failing or creating a duplicate.
5. **Error isolation** — A failure in one file must not abort the processing of the remaining files.
6. **Summary on completion** — After all files are processed, show an overall summary (total conversations imported, skipped, and failed across all files).

## Acceptance Criteria

- [x] The upload UI accepts multiple `.json` files in a single selection.
- [x] All selected files are uploaded and processed without requiring the user to repeat the flow.
- [x] Progress is visible for each file.
- [x] Conversations that already exist in the database are skipped gracefully (no duplicates, no errors).
- [x] A failure processing one file does not prevent the other files from being processed.
- [x] A summary screen shows totals across all uploaded files.
- [x] Existing single-file upload behaviour continues to work unchanged.

## Notes

- The `InputFile` Blazor component supports `multiple` — set the `multiple` attribute to enable multi-file selection.
- The background processing pipeline (issue 006) likely needs only minor adjustment to accept a list of files or to be invoked once per file.
- Consider whether the duplicate-detection logic belongs in the parser, the MongoDB upsert, or a separate pre-check.
- Keep the UI changes minimal; the goal is reliable import, not a polished file manager.
