# 005 — Create File Upload UI Page with Progress Tracking

**Status:** TODO
**Sequence:** 5
**Dependencies:** None (UI scaffold, backend wiring comes in issue 006)

## Summary

Add a Blazor page to the web frontend that allows users to upload their ChatGPT `conversations.json` file. Since the file may be very large (~148 MB), the UI must handle large file uploads gracefully and show progress/status.

## Requirements

1. Add a new Blazor page at `/upload` in `MattGPT.Web`.
2. The page should include:
   - A file picker that accepts `.json` files.
   - A button to start the upload/processing.
   - A progress indicator (progress bar or status text) that updates during processing.
   - Display of completion status (success, error, summary of what was processed).
3. Configure Blazor and Kestrel to handle large file uploads (increase max request body size as needed).
4. The file should be streamed to the API service — do not buffer the entire file in browser memory.
5. Add navigation to the upload page from the existing nav menu.

## Acceptance Criteria

- [ ] A `/upload` page exists and is accessible from the nav menu.
- [ ] Users can select a `.json` file and initiate upload.
- [ ] The UI handles files up to at least 200 MB without crashing or timing out.
- [ ] Progress feedback is visible during upload.
- [ ] Error states (wrong file type, upload failure) are handled gracefully in the UI.

## Notes

- At this stage the backend endpoint to receive the file may be a stub that accepts the stream and returns a mock response. Full processing is wired in issue 006.
- Consider using `InputFile` component with streaming or a chunked upload approach.
- Large file upload configuration may need Kestrel `MaxRequestBodySize` adjustment.
