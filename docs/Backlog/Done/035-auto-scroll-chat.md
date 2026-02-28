# 035 — Auto-Scroll Chat to Bottom

**Status:** Done  
**Sequence:** 35  
**Dependencies:** 012 (chat UI)

## Summary

Automatically scroll the chat message area to the bottom when new messages are added or when streaming tokens arrive. Currently the chat area (`#chat-messages` div with `overflow-y-auto`) does not auto-scroll, so users must manually scroll down to see new content as it streams in or after sending a message.

## Background

The chat message area is a `<div class="flex-1 overflow-y-auto px-4 py-4" id="chat-messages">` in `Chat.razor`. When conversations are long enough to require scrolling, new messages and streaming tokens appear below the visible fold. There is no JavaScript interop or scroll management — the browser simply keeps the scroll position where it was.

This is particularly noticeable when:
- Loading a long imported conversation (user lands at the top, not the most recent messages).
- Sending a new message in an active chat (the response streams in below the fold).
- The LLM produces a long response that overflows the visible area.

## Requirements

1. **Auto-scroll on new messages** — When the user sends a message, scroll to the bottom of the chat area so the new message and subsequent LLM response are visible.

2. **Auto-scroll during streaming** — As streaming tokens arrive and the response grows, keep scrolling to the bottom so the user can read the response as it's generated.

3. **Respect manual scroll position** — If the user has manually scrolled up to review earlier messages, do NOT force-scroll them back to the bottom while tokens are streaming. Only auto-scroll if the user is already at or near the bottom. Resume auto-scrolling once the user scrolls back to the bottom.

4. **Scroll on conversation load** — When loading an imported conversation or switching to an existing chat session, scroll to the bottom to show the most recent messages.

5. **Smooth scrolling** — Use smooth scroll behaviour for a polished feel, but ensure it doesn't lag behind fast-streaming tokens.

## Acceptance Criteria

- [x] Sending a message scrolls the chat area to the bottom.
- [x] Streaming response tokens keep the view scrolled to the bottom.
- [x] Manually scrolling up pauses auto-scroll; scrolling back to the bottom resumes it.
- [x] Loading a conversation (imported or native) scrolls to the bottom.
- [x] Scrolling is smooth and does not cause visual jank.

## Notes

- Implementation will likely require JavaScript interop (`IJSRuntime`) since Blazor Server doesn't have direct DOM scroll APIs. A small JS function like `scrollToBottom('chat-messages')` called via interop after each `StateHasChanged` should suffice.
- Consider using `IntersectionObserver` or a scroll position check to detect whether the user is near the bottom before auto-scrolling.
- The `id="chat-messages"` already exists on the scroll container, so it's easy to target.
