# 033 — SPA-Style Chat Navigation

**Status:** TODO  
**Sequence:** 33  
**Dependencies:** 022 (chat history sidebar), 012 (chat UI)

## Summary

Improve chat navigation so that clicking a conversation in the sidebar only reloads the chat message area rather than causing a full page re-render. Currently, clicking an imported conversation triggers `Navigation.NavigateTo(...)` which updates the URL and can cause a perceptible full-page refresh of the Blazor circuit.

## Background

The current navigation behaviour in the chat UI:

- **Clicking a native chat session**: Calls `OnSessionSelected.InvokeAsync(session.SessionId)` which invokes `LoadSession(Guid)` in `Chat.razor`. This fetches the session data and calls `StateHasChanged()` — **no URL change**, so re-render is partial. This works well.
- **Clicking an imported conversation**: Calls `OnConversationSelected.InvokeAsync(conv.ConversationId)` which invokes `LoadImportedConversation(string)`. This fetches the conversation data **and** calls `Navigation.NavigateTo($"/chat/conversation/{conversationId}", replace: true)` which updates the URL. This can cause a more noticeable re-render.
- The Chat page has two `@page` directives: `/chat` and `/chat/conversation/{ConversationId}`, with `OnParametersSetAsync` handling deep linking for the conversation route.

The goal is to make all navigation feel instant and SPA-like — only the message area should update, not the entire page including the sidebar.

## Requirements

1. **Consistent navigation model** — Both native sessions and imported conversations should navigate via component state updates (like native sessions already do), not via URL-based navigation that triggers full re-renders.

2. **Preserve deep linking** — Imported conversation URLs (`/chat/conversation/{id}`) should still work for direct links and browser refresh. Consider updating the URL via `replaceState` (JavaScript interop) without triggering Blazor navigation, or handle the route parameter in `OnParametersSetAsync` only on initial load.

3. **Sidebar state preservation** — When switching between conversations, the sidebar should maintain its scroll position, expanded/collapsed state, and filter text.

4. **Smooth transitions** — The message area should show a brief loading indicator while fetching a new conversation, but the sidebar and page chrome should remain stable.

## Acceptance Criteria

- [ ] Clicking any conversation (native or imported) in the sidebar updates only the chat message area.
- [ ] The sidebar does not visibly re-render or lose state when switching conversations.
- [ ] Deep links to imported conversations (`/chat/conversation/{id}`) still work on initial page load.
- [ ] Navigation between conversations feels instant and smooth.

## Notes

- This may be a Blazor Server rendering nuance. Investigate whether `NavigateTo` with `replace: true` and `forceLoad: false` already avoids full circuit re-creation, or if the perceived flicker is just the `OnParametersSetAsync` chain re-executing.
- An alternative approach: always stay on `/chat` and manage conversation state entirely via component state, using JS interop to update the browser URL for deep-link support without triggering Blazor navigation.
- Test on both fast and slow connections — the improvement should be noticeable even on localhost.
