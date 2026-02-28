# 029 — System Prompt and User Profile UI

**Status:** TODO  
**Sequence:** 29  
**Dependencies:** 028 (extract user profile), 012 (chat UI)

## Summary

Add a settings page to the Blazor UI where users can view and edit the system prompt and user profile (custom instructions). The backend already supports storing a user profile via `UserProfileRepository`, and the system prompt is defined in `RagService.BuildMessages`, but neither is exposed for editing through the UI.

## Background

Issue 028 implemented extraction of the user's ChatGPT custom instructions (`user_profile` and `user_instructions`) during import. These are stored in MongoDB and injected into the system prompt in `RagService.BuildMessages` under `=== USER CONTEXT ===`. However:

- There is no API endpoint to read or update the user profile or system prompt.
- There is no settings page in the Blazor UI — `NavMenu.razor` only has "Upload" and "Chat" links.
- The system prompt itself is hardcoded in `RagService.cs` (the "You are a knowledgeable personal assistant..." block).

ADR-002 acknowledged that building a custom Blazor UI means features like settings must be implemented manually. This issue adds the minimum viable settings surface.

## Requirements

1. **API endpoints** — Add `GET /user-profile` and `PUT /user-profile` endpoints to read and update the stored user profile (`UserProfileText` and `UserInstructions` fields).

2. **System prompt endpoint** — Add `GET /system-prompt` and `PUT /system-prompt` endpoints. The system prompt should be stored in MongoDB (or a config store) rather than hardcoded, so it can be edited at runtime. Provide a sensible default matching the current hardcoded value.

3. **Settings page** — Add a `/settings` page to the Blazor UI with:
   - A text area showing and allowing edits to the system prompt.
   - Text areas showing and allowing edits to the user profile ("About me") and user instructions ("How should the assistant respond").
   - A save button that persists changes via the API.
   - A reset-to-defaults button for the system prompt.

4. **Navigation** — Add a "Settings" link to `NavMenu.razor`.

## Acceptance Criteria

- [ ] `GET /user-profile` returns the current user profile.
- [ ] `PUT /user-profile` updates the user profile fields.
- [ ] `GET /system-prompt` returns the current system prompt (stored, not hardcoded).
- [ ] `PUT /system-prompt` updates the system prompt.
- [ ] A `/settings` page exists in the Blazor UI showing editable system prompt, user profile, and user instructions.
- [ ] Changes made on the settings page are persisted and take effect in subsequent chat sessions.
- [ ] A "Settings" link appears in the navigation menu.

## Notes

- Borderline with ADR-002 ("Switch to OpenWebUI"), but this is a small, self-contained settings surface that improves usability significantly.
- Consider showing a preview of how the system prompt + user context will look when combined.
- The system prompt should probably be stored in a new MongoDB collection (e.g. `system_config`) to keep it separate from user profile data.
