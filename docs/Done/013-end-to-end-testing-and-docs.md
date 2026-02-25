# 013 — End-to-End Testing and Documentation

**Status:** Done
**Sequence:** 13
**Dependencies:** All previous issues

## Summary

Validate the complete pipeline end-to-end and create the minimal documentation described in the README.

## Requirements

### Testing

1. Perform an end-to-end test:
   - Start the Aspire application with `aspire run`.
   - Upload a ChatGPT conversation export via the UI.
   - Verify conversations are parsed, stored in MongoDB, summarised, embedded, and stored in Qdrant.
   - Use the chat UI to query against the stored conversations and verify RAG-augmented responses.
2. Test with both a small sample file and the full ~148 MB export if available.
3. Test LLM provider switching (at least two of: Ollama, Foundry Local, Azure OpenAI).
4. Document any bugs found and create follow-up issues as needed.

### Documentation

1. **Running locally**: Instructions for running the Aspire project, including prerequisites (Docker, .NET SDK version, etc.) and setup steps.
2. **Uploading and processing**: How to export ChatGPT data, expected JSON format, file size limitations, and what to expect during processing.
3. **Testing LLM interaction**: How to use the chat UI, switch LLM providers, and interpret results.
4. **Troubleshooting**: Common issues and how to resolve them.

Place documentation in appropriate locations (README updates, `docs/` folder, or inline in the UI).

## Acceptance Criteria

- [x] The full pipeline works end-to-end from upload to RAG-augmented chat.
- [x] Documentation covers all four areas listed above.
- [x] At least two LLM providers are tested and confirmed working.
- [x] No critical bugs remain open.
- [x] The README is updated with final setup and usage instructions.

## Notes

- This is the capstone issue. It may spawn additional follow-up issues for bugs or improvements discovered during testing.
- Consider adding a sample/test JSON file to the repo for quick validation.
