# 006 — Implement Background Processing Service for Import

**Status:** TODO
**Sequence:** 6
**Dependencies:** 004 (parser), 005 (upload UI)

## Summary

Implement a background processing pipeline that receives uploaded conversation files, parses them using the ChatGPT JSON parser, and orchestrates the downstream processing steps. Processing must happen on a background thread with observable progress in the UI.

## Requirements

1. Create an API endpoint in `MattGPT.ApiService` to receive the uploaded file as a stream.
2. Implement a background processing service (e.g. `IHostedService`, `BackgroundService`, or a channel-based queue) that:
   - Accepts a file stream for processing.
   - Uses the parser from issue 004 to stream-parse conversations.
   - Tracks progress (conversations parsed, total estimated, errors).
   - Exposes progress via an API endpoint (polling) or SignalR (push).
3. Wire the upload UI (issue 005) to call the API endpoint and poll/subscribe for progress updates.
4. Handle errors gracefully — a single malformed conversation should not abort the entire import.

## Acceptance Criteria

- [ ] Uploading a file triggers background processing that does not block the HTTP response.
- [ ] Progress is observable in the UI (number of conversations processed, percentage, errors).
- [ ] The processing pipeline can handle the full ~148 MB file without running out of memory.
- [ ] Individual conversation parsing errors are logged but do not stop the import.
- [ ] Processing status persists across page refreshes (stored in-memory or in the database).

## Notes

- At this stage, "processing" means parsing and counting. Storage in MongoDB (issue 007) and downstream steps (issues 008–010) will be wired later.
- Consider SignalR for real-time progress push to the Blazor UI, but polling is acceptable for the MVP.
