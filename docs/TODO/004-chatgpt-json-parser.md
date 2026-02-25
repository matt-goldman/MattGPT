# 004 — Build ChatGPT JSON Parser & Conversation Lineariser

**Status:** TODO
**Sequence:** 4
**Dependencies:** None (pure logic, no infrastructure dependencies)

## Summary

Build a parser that can stream-read a large ChatGPT `conversations.json` export (~148 MB), extract individual conversations, and linearise each conversation's message tree into a flat sequence of turns.

## Requirements

1. **Streaming parser**: Use `System.Text.Json` with streaming APIs (`Utf8JsonReader` or `JsonSerializer.DeserializeAsyncEnumerable`) to avoid loading the entire file into memory.
2. **Data model**: Define C# models for the relevant parts of the ChatGPT export schema (reference `conversations.schema.json` in the repo root). At minimum:
   - `Conversation` (id, title, create_time, update_time, mapping, current_node, default_model_slug)
   - `MappingNode` (id, message, parent, children)
   - `Message` (id, author, content, create_time)
   - `Author` (role, name, metadata)
   - `ContentParts` (content_type, parts/text)
3. **Tree linearisation**: Given a conversation's `mapping` and `current_node`, walk from `current_node` back through parent pointers to the root, reverse the result to produce the active thread as a flat `List<Message>`.
4. **Chunking**: Each conversation is one chunk. The parser should yield conversations as an `IAsyncEnumerable<ParsedConversation>` where `ParsedConversation` contains the conversation metadata and the linearised messages.
5. **Unit tests**: Cover tree linearisation (branching, single-thread, empty conversations) and basic parsing of a small sample JSON.

## Acceptance Criteria

- [ ] Parser can stream a multi-GB JSON file without loading it entirely into memory.
- [ ] Message trees are correctly linearised to the active thread.
- [ ] Branching conversations (edits/regenerations) are handled — only the active branch is linearised.
- [ ] Unit tests pass for parsing and linearisation.
- [ ] The parser is a standalone service/class with no dependency on databases or LLMs.

## Notes

- The `conversations.schema.json` in the repo root has the full schema. Focus on the fields listed above; ignore unused fields for now.
- Consider placing the parser in a shared library project if it will be used by multiple services, or keep it in the API service for now and refactor later.
