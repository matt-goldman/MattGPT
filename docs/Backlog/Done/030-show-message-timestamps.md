# 030 — Show Message and Conversation Timestamps

**Status:** Done  
**Sequence:** 30  
**Dependencies:** 012 (chat UI), 022 (chat history sidebar)

## Summary

Display date and time information on individual messages in the chat UI. Currently the sidebar shows relative dates for chat sessions and formatted dates for imported conversations, but individual messages within a conversation do not show any timestamp despite the data being available.

## Background

The data models already carry timestamp information:

- `ChatSessionMessage` has a `Timestamp` (`DateTimeOffset`) field.
- `StoredMessage` (imported) has a `CreateTime` (`double?`, Unix epoch) field.
- The DTOs returned by the API include these fields.

The sidebar already formats dates:
- "Your Chats" shows relative time (e.g. "5m ago", "2h ago") via `FormatRelativeDate`.
- "Imported History" shows "MMM d, yyyy" via `FormatImportedDate`.

However, the message rendering loop in `Chat.razor` (`@foreach (var msg in _messages)`) does not display timestamps for individual messages.

## Requirements

1. **Message timestamps** — Show a timestamp on each message bubble in the chat UI. Use a subtle, non-intrusive format (e.g. "2:34 PM" for today, "Feb 15, 2:34 PM" for older messages, or relative time for very recent messages).

2. **Date separators** — When a conversation spans multiple days, show a date separator between messages from different days (similar to how messaging apps group messages by date).

3. **Imported conversations** — Convert Unix epoch `CreateTime` to a human-readable local time format.

4. **Consistent formatting** — Use the same time formatting approach across both native chat sessions and imported conversations.

## Acceptance Criteria

- [x] Each message in the chat UI displays its timestamp.
- [x] Timestamps are formatted appropriately (time-only for today, date+time for older messages).
- [x] Date separators appear between messages from different days in long conversations.
- [x] Both native chat session messages and imported conversation messages show timestamps.
- [x] Timestamps do not clutter the UI — they should be subtle and secondary to the message content.

## Notes

- Consider making timestamps toggle-able or showing them on hover to keep the UI clean.
- The sidebar already has formatting helpers (`FormatRelativeDate`, `FormatImportedDate`) that could be reused or adapted.
- For imported conversations, some messages may have `null` `CreateTime` — handle gracefully (hide the timestamp or show "Unknown").
