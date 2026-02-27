# ChatGPT Export Content Analysis

> Findings report for MattGPT issue 017.  
> Analysis performed on 2026-02-27 against 8 export files covering **2,913 conversations** and **79,910 messages**.

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [JSON Schema Audit](#2-json-schema-audit)
3. [Content-Type Inventory](#3-content-type-inventory)
4. [Rich Media Assessment](#4-rich-media-assessment)
5. [Memory and System Prompt Fields](#5-memory-and-system-prompt-fields)
6. [Metadata Completeness](#6-metadata-completeness)
7. [Gap Summary and Recommendations](#7-gap-summary-and-recommendations)

---

## 1. Executive Summary

The current MattGPT import pipeline (parser → MongoDB → summariser → embedder) was designed for MVP: it extracts the active message thread and flattens each message's content `parts` into strings. This works well for simple text conversations but discards or degrades a significant volume of structured data that has high value for RAG retrieval.

### Scale of what's being missed

| Category | Count | Currently captured? |
|----------|------:|:-------------------:|
| Text messages (content_type = `text`) | 66,123 | **Yes** (string parts) |
| Multimodal messages (`multimodal_text`) | 1,569 | Partially — image pointers become raw JSON strings |
| Code interpreter code (`code`) | 1,931 | `text` field stored but content_type info flattened |
| Code execution output (`execution_output`) | 773 | `text` field stored but content_type info flattened |
| Web browse results (`tether_quote`) | 5,161 | `text` field stored but URL/domain/title lost |
| Web browse summaries (`tether_browsing_display`) | 1,244 | Poorly — uses `result`/`summary` fields, not `parts` |
| Custom instructions (`user_editable_context`) | 1,170 | Included as messages but not extracted as profile |
| Reasoning recaps (`reasoning_recap`) | 788 | Uses `content` field, not `parts` — likely empty Parts |
| Thoughts/thinking (`thoughts`) | 1,042 | Uses `thoughts` array, not `parts` — likely empty Parts |
| System errors | 81 | `text` field captured |
| Computer output (Operator) | 15 | Uses `screenshot`/`state` — not captured |
| Citable code output | 13 | Uses `output_str` — not captured |
| **File attachments** (on message metadata) | **7,999** | **No — completely lost** |
| **Citations** (file/web source refs) | **8,651** | **No — completely lost** |
| **Content references** (rich links) | **13,106** | **No — completely lost** |
| **Image asset pointers** (in parts) | **1,378** | Stored as raw JSON, not parsed |
| **Canvas documents** | **854** | **No — completely lost** |
| **Code execution results** (aggregate_result) | **773** | **No — completely lost** |
| **Search result groups** | **798** | **No — completely lost** |

### Impact on RAG quality

The most significant gaps for RAG are:

1. **Tool calls and results are opaque.** When a user asks ChatGPT to run code, browse the web, or generate images, the _purpose_ and _outcome_ are spread across assistant→tool and tool→assistant message pairs. The parser includes all these messages but treats tool output as a raw text blob, losing the structured information (URLs found, code executed, images generated).

2. **File attachments are invisible.** 7,999 attachment records (filenames, MIME types, sizes) on user messages are stored only in `metadata.attachments`, which the `Message` model doesn't capture. This means the embedder has no idea that a conversation involved specific files.

3. **Citations and references are lost.** 8,651 citations and 13,106 content references provide precise links between assistant text and source material. These would be extremely valuable for RAG — they could power source-linked retrieval.

4. **Non-text content fields are ignored.** Content types like `code`, `tether_quote`, `tether_browsing_display`, and `execution_output` use the `text`, `result`, `summary`, `url`, and `domain` fields instead of (or in addition to) `parts`. The current `StoredMessage.From()` method only reads `parts`, so a `tether_quote` message loses its URL, domain, and title — keeping only the raw quote text if it happens to appear in `parts`.

---

## 2. JSON Schema Audit

### Conversation-level fields

| Field | In schema | In export (non-null) | Captured by parser | RAG value | Recommendation |
|-------|:---------:|---------------------:|:------------------:|:---------:|---------------|
| `id` | ✓ | 2,913 | ✓ | Key | — |
| `title` | ✓ | 2,902 | ✓ | High | — |
| `create_time` | ✓ | 2,913 | ✓ | Medium | — |
| `update_time` | ✓ | 2,913 | ✓ | Medium | — |
| `current_node` | ✓ | 2,913 | ✓ (for linearisation) | Internal | — |
| `mapping` | ✓ | 2,913 | ✓ | Core | — |
| `default_model_slug` | ✓ | 2,098 | ✓ | Low–Med | Already captured |
| `conversation_id` | ✓ | 2,913 | ✗ | None (duplicates `id`) | Ignore |
| `moderation_results` | ✓ | 2,913 (empty arrays) | ✗ | None | Ignore |
| `gizmo_id` | ✓ | 758 | ✗ | **Medium** | **New issue** — enables "which custom GPT" filtering |
| `gizmo_type` | ✓ | 758 | ✗ | Low | Capture alongside gizmo_id |
| `conversation_template_id` | ✓ | 758 | ✗ | **Medium** | **New issue** — correlates with GPT/project context |
| `plugin_ids` | ✓ | 65 | ✗ | Low | Legacy; ignore |
| `is_archived` | ✓ | 2,913 | ✗ | Low | Could filter; low priority |
| `is_starred` | ✓ | 4 | ✗ | Low | Negligible count |
| `is_do_not_remember` | ✓ | 1,026 | ✗ | **Medium** | **New issue** — should respect memory opt-out |
| `memory_scope` | ✓ | 2,913 | ✗ | **Medium** | Capture with above |
| `safe_urls` / `blocked_urls` | ✓ | 2,913 | ✗ | None | Ignore |
| `disabled_tool_ids` | ✓ | 2,913 | ✗ | None | Ignore |
| `voice` | ✓ | 0 | ✗ | None | Ignore |
| `async_status` | ✓ | 25 | ✗ | Low | Ignore |
| `is_study_mode` | ✓ | 2,913 | ✗ | None | Ignore |
| `sugar_item_id` / `sugar_item_visible` | ✓ | 2,913 | ✗ | None | Internal UI; ignore |
| `pinned_time` | ✓ | 2 | ✗ | Low | Negligible |
| `conversation_origin` | ✓ | 0 | ✗ | None | Ignore |
| `is_read_only` | ✓ | 0 | ✗ | None | Ignore |
| `context_scopes` | ✓ | 0 | ✗ | None | Ignore |
| `owner` | ✓ | 0 | ✗ | None | Ignore |

### Message-level fields

| Field | In schema | Present (non-null) | Captured | RAG value | Recommendation |
|-------|:---------:|-------------------:|:--------:|:---------:|---------------|
| `id` | ✓ | 79,910 | ✓ | Key | — |
| `author.role` | ✓ | 79,910 | ✓ | High | — |
| `author.name` | ✓ | 11,719 | ✗ | **High** | **New issue** — identifies which tool produced the message |
| `content` | ✓ | 79,910 | ✓ (partially) | Core | See §3 |
| `create_time` | ✓ | 79,910 | ✓ | Medium | — |
| `update_time` | ✓ | varies | ✗ | Low | Ignore |
| `status` | ✓ | 79,672 | ✗ | Low | Could filter `in_progress`; ignore for now |
| `end_turn` | ✓ | varies | ✗ | Low | Ignore |
| `weight` | ✓ | 79,910 | ✗ | **Medium** | Weight 0.0 = system/hidden. Could filter. See §5 |
| `recipient` | ✓ | 79,910 | ✗ | **Medium** | **New issue** — identifies tool dispatch targets |
| `channel` | ✓ | 2,712 | ✗ | Low | Ignore (only `final`/`commentary`) |
| `metadata` | ✓ | 79,910 | ✗ | **HIGH** | See below — richest source of lost data |

### Message metadata fields (most impactful)

| Field | Count | Captured | RAG value | Recommendation |
|-------|------:|:--------:|:---------:|---------------|
| `model_slug` | 48,963 | ✗ | Medium | **New issue** — useful for per-message model attribution |
| `attachments` | 2,210 messages (7,999 items) | ✗ | **HIGH** | **New issue** — file names/types are critical RAG context |
| `citations` | 24,588 messages (8,651 items) | ✗ | **HIGH** | **New issue** — source attribution for retrieval |
| `content_references` | 22,551 messages (13,106 items) | ✗ | **HIGH** | **New issue** — rich typed references |
| `aggregate_result` | 773 | ✗ | **Medium** | **New issue** — code execution results |
| `canvas` | 854 | ✗ | **Medium** | **New issue** — canvas document context |
| `search_result_groups` | 984 | ✗ | Medium | Capture with web browse improvements |
| `reasoning_title` / `reasoning_status` | 918–3,409 | ✗ | Low | Reasoning is interesting but low RAG value |
| `finish_details` | 26,136 | ✗ | None | Ignore |
| `is_visually_hidden_from_conversation` | 17,107 | ✗ | **Medium** | **New issue** — should filter hidden messages |
| `dictation` | 5,911 | ✗ | None | Ignore (3 actual dictated, rest are `false`) |
| `async_task_id/type` | 27 | ✗ | Low | Deep research; low count |
| `gizmo_id` (message-level) | 9,340 | ✗ | Low–Med | Capture with conv-level gizmo_id |

---

## 3. Content-Type Inventory

### Overview

12 distinct `content_type` values found across all messages:

| Content Type | Count | % of messages | Current handling | Gap severity |
|-------------|------:|:------------:|:----------------|:------------:|
| `text` | 66,123 | 82.7% | ✅ Parts extracted as strings | None |
| `tether_quote` | 5,161 | 6.5% | ⚠️ Text extracted if in `parts`; URL/domain/title **lost** | **HIGH** |
| `code` | 1,931 | 2.4% | ⚠️ `text` field goes to `Parts` but content_type not distinguished | Medium |
| `multimodal_text` | 1,569 | 2.0% | ⚠️ String parts OK; image_asset_pointers become raw JSON | **HIGH** |
| `tether_browsing_display` | 1,244 | 1.6% | ❌ Uses `result`/`summary` not `parts` — **empty Parts** | **HIGH** |
| `user_editable_context` | 1,170 | 1.5% | ⚠️ Included but `user_profile`/`user_instructions` not extracted | Medium |
| `thoughts` | 1,042 | 1.3% | ❌ Uses `thoughts` array, not `parts` — **empty Parts** | Medium |
| `reasoning_recap` | 788 | 1.0% | ❌ Uses `content` field, not `parts` — **empty Parts** | Low |
| `execution_output` | 773 | 1.0% | ⚠️ `text` field may not reach `Parts` | Medium |
| `system_error` | 81 | 0.1% | ⚠️ `text`/`name` fields; `parts` absent | Low |
| `computer_output` | 15 | <0.1% | ❌ Uses `screenshot`/`state`; no text content | Low |
| `citable_code_output` | 13 | <0.1% | ❌ Uses `output_str`; `parts` absent | Low |

### Detailed analysis per content type

#### `text` (66,123 messages)
- **Structure:** `{ "content_type": "text", "parts": ["..."] }`
- **Current handling:** Fully captured. Parts are string arrays.
- **Gap:** None for text content itself. However, the _metadata_ on these messages (citations, references, attachments) is lost.

#### `tether_quote` (5,161 messages)
- **Structure:** `{ "content_type": "tether_quote", "url": "...", "domain": "...", "title": "...", "text": "...", "tether_id": ... }`
- **Current handling:** The `Content` model has a `Text` property that would be deserialised, but `Parts` is null for this type. The `StoredMessage.From()` method reads `Parts` only, so the quote text is only captured if it appears there. The URL, domain, and title are **always lost**.
- **Gap:** **HIGH** — 5,161 web quotes with source URLs are reduced to either truncated text or nothing.
- **URL breakdown:** 4,965 use `file-id` references (file-service managed), 187 use `https://` web URLs.
- **Recommendation:** Extract `text`, `url`, `domain`, `title` into structured fields on `StoredMessage`.

#### `code` (1,931 messages)
- **Structure:** `{ "content_type": "code", "language": "python", "text": "import pandas as pd\n..." }`
- **Current handling:** The `text` field is deserialised by `Content.Text`, and since `Parts` is null, `StoredMessage.Parts` is empty. The code text is **lost**.
- **Gap:** **HIGH** — 1,931 code blocks (mostly Python from code interpreter) are not stored.
- **Recommendation:** When `content_type == "code"`, use `Content.Text` as the stored content. Preserve `language` metadata.

#### `multimodal_text` (1,569 messages)
- **Structure:** `{ "content_type": "multimodal_text", "parts": ["text", { "content_type": "image_asset_pointer", "asset_pointer": "file-service://...", ... }, "more text"] }`
- **Current handling:** String parts are captured. Object parts (image_asset_pointer) are serialised as raw JSON via `GetRawText()`. The embedding/summary pipeline sees something like `{"content_type":"image_asset_pointer","asset_pointer":"file-service://file-XWZ..."}` — which is noise.
- **Gap:** **HIGH** — 1,378 image references within these messages. The _text_ parts around the images carry context but image references are opaque.
- **Recommendation:** For image_asset_pointer objects, extract a meaningful placeholder like `[Image: DALL-E generated image]` or `[Uploaded image: 1200×1600]`. Store the asset_pointer for potential future retrieval.

#### `tether_browsing_display` (1,244 messages)
- **Structure:** `{ "content_type": "tether_browsing_display", "result": "...", "summary": "..." }`
- **Current handling:** `Parts` is null for this type, so `StoredMessage.Parts` is empty. The `result` and `summary` text fields are **completely lost**.
- **Gap:** **HIGH** — browse summaries are substantive content that provides web-sourced context.
- **Recommendation:** Extract `result` and/or `summary` as stored content.

#### `user_editable_context` (1,170 messages)
- **Structure:** `{ "content_type": "user_editable_context", "user_profile": "...", "user_instructions": "..." }`
- **Current handling:** `Parts` is null; both fields are **completely lost**. These are system messages injected at the start of each conversation containing the user's custom instructions.
- **Gap:** **Medium** — the custom instructions are the same across most conversations and would add noise if embedded with every conversation. However, they're valuable as a single reference document.
- **Recommendation:** Extract once and store as a user profile document. Do not embed per-conversation.

#### `thoughts` (1,042 messages)
- **Structure:** `{ "content_type": "thoughts", "thoughts": [{"content": "Let me think about...", "summary": "Thinking..."}], "source_analysis_msg_id": "..." }`
- **Current handling:** `Parts` is null; thoughts array is **lost**.
- **Gap:** **Medium** — thinking/reasoning traces can contain useful intermediate analysis.
- **Recommendation:** Extract `thoughts[].content` as supplementary text. Lower priority for RAG — the final answer captures the conclusion.

#### `reasoning_recap` (788 messages)
- **Structure:** `{ "content_type": "reasoning_recap", "content": "Thought for a couple of seconds" }`
- **Current handling:** `Parts` is null; `content` string is **lost**.
- **Gap:** **Low** — these are short summary lines like "Thought for 30 seconds". Minimal RAG value.
- **Recommendation:** Ignore or store as annotation.

#### `execution_output` (773 messages)
- **Structure:** `{ "content_type": "execution_output", "text": "..." }`
- **Current handling:** `Parts` is null; `text` field is **lost**.
- **Gap:** **Medium** — code execution output (stderr/stdout, tracebacks, results) provides context about what happened.
- **Recommendation:** Extract `text` as content. Pair with the preceding `code` message for full context.

#### `system_error` (81 messages)
- **Structure:** `{ "content_type": "system_error", "name": "GetDownloadLinkError", "text": "..." }`
- **Current handling:** `Parts` is null; error details **lost**.
- **Gap:** **Low** — errors are not RAG-relevant.
- **Recommendation:** Store `name` and `text` for completeness, but low priority.

#### `computer_output` (15 messages)
- **Structure:** `{ "content_type": "computer_output", "computer_id": "0", "screenshot": { image_asset_pointer }, "state": { "type": "browser_state", "url": "...", "title": "..." } }`
- **Current handling:** `Parts` absent; screenshot and state **lost**.
- **Gap:** **Low** — only 15 occurrences (Operator feature). The `state.url` and `state.title` have RAG value.
- **Recommendation:** Extract `state.title` and `state.url` as text content. Low priority.

#### `citable_code_output` (13 messages)
- **Structure:** `{ "content_type": "citable_code_output", "output_str": "{...json...}" }`
- **Current handling:** `Parts` absent; output **lost**.
- **Gap:** **Low** — only 13 occurrences.
- **Recommendation:** Extract `output_str` as text. Low priority.

---

## 4. Rich Media Assessment

### Image assets (1,378 references)

**How referenced:** Images appear as `image_asset_pointer` objects inside `parts` arrays of `multimodal_text` messages, and as `screenshot` fields in `computer_output` content.

**URI schemes:**
| Scheme | Count | Description |
|--------|------:|-------------|
| `file-service://` | 1,052 | User-uploaded images; served by OpenAI's file service. Not accessible outside ChatGPT. |
| `sediment://` | 326 | DALL-E generated or system images; internal storage. Not externally accessible. |

**Metadata on images:**
- `sanitized` (1,348) — content safety flag
- `dalle` (385) — DALL-E generation metadata (prompt, seed, etc.)
- `generation` (224) — generation details
- `container_pixel_width`/`container_pixel_height` (224) — display size
- `width`/`height` — actual pixel dimensions
- `size_bytes` — file size

**Can images be retrieved?** **No.** Both `file-service://` and `sediment://` URIs are internal to OpenAI's infrastructure. The export does not include the actual image binary data. The ChatGPT web UI has special auth to render these; they cannot be fetched externally.

**Recommendation:**
- Do **not** attempt to download/store images — they are inaccessible.
- **Do** extract descriptive placeholders from the context: for DALL-E images, the preceding assistant message typically contains the generation prompt. For uploaded images, the surrounding text usually describes what was uploaded.
- Store the `asset_pointer` URI and dimensions as metadata for potential future use if OpenAI ever adds export of media.

### File attachments (7,999 items on 2,210 messages)

**How referenced:** Attachments are in `message.metadata.attachments[]`. Each has an `id` (file reference), `name` (original filename), `mime_type`/`mimeType`, `size`, and optionally `width`/`height` for images and `file_token_size`/`fileSizeTokens` for text files.

**MIME type distribution (top 10):**
| MIME Type | Count | Category |
|-----------|------:|----------|
| `text/markdown` | 2,174 | Code/text |
| `text/x-csharp` | 1,686 | Code |
| `image/png` | 1,420 | Image |
| `text/plain` | 751 | Text |
| `text/javascript` | 270 | Code |
| `text/css` | 254 | Code |
| `text/tsx` | 176 | Code |
| `image/jpeg` | 149 | Image |
| `video/mp2t` | 140 | Video (TypeScript .ts misidentified) |
| `application/pdf` | 98 | Document |

**Notable observations:**
- `video/mp2t` (140 items) is almost certainly `.ts` TypeScript files being misidentified by MIME type detection — the MPEG-TS MIME type shares the `.ts` extension.
- Attachment sources: only 281 marked as `local`, rest have no source field.
- Attachment field naming is inconsistent: some use camelCase (`mimeType`, `fileSizeTokens`), others use snake_case (`mime_type`, `file_token_size`). Both variants must be handled.

**Can files be retrieved?** **No.** File IDs (e.g. `file-XWZMtijEMLcNnEu79zWNP4`) reference OpenAI's file service and cannot be downloaded externally.

**Recommendation:**
- Store attachment metadata (filename, MIME type, size) on StoredMessage.
- The filename alone is highly valuable for RAG — knowing a conversation involved `PrismCodeBlockRenderer.cs` or `Episode_36.vtt` is rich searchable context.
- Create a new field on `StoredMessage` for attachments.

### Audio/voice

- Only 3 messages with `dictation: true` (voice input).
- `dictation_asset_pointer` format: audio files on OpenAI's file service. Not retrievable.
- **Recommendation:** Ignore — minimal count, no accessible audio.

### DALL-E generated images

- 385 images with DALL-E metadata (328 from `dalle.text2im` author).
- The generation prompt is typically in the preceding assistant message (recipient = `dalle.text2im`).
- **Recommendation:** When processing an image_asset_pointer with `dalle` metadata, look for the preceding tool-call message to capture the generation prompt as text.

---

## 5. Memory and System Prompt Fields

### Custom instructions (`user_editable_context`)

- **1,170 messages** with content type `user_editable_context`
- Contains two fields:
  - `user_profile` — the "About me" custom instruction (e.g. "I am a .NET developer...")
  - `user_instructions` — the "How should ChatGPT respond" instruction (e.g. "I generally don't need background info...")
- These are injected as system messages at the start of most conversations.
- **Currently:** Completely lost (Parts is null for this content type).
- **Recommendation:** Extract the latest `user_profile` and `user_instructions` and store as a separate user profile document. Don't embed these per-conversation — they're identical across most conversations and would add noise. Could be useful as system context for MattGPT's own chat.

### Memory scope

- **2,913 conversations** have `memory_scope` set:
  - `global_enabled` — 2,759 (memory/recall active)
  - `project_enabled` — 154 (project-scoped memory)
- **1,026 conversations** have `is_do_not_remember: true`.
- **Recommendation:** Capture `memory_scope` and `is_do_not_remember` and respect opt-out. Conversations marked `is_do_not_remember` might warrant exclusion from RAG indexing (or at least flagging) since the user explicitly chose to not have that conversation remembered.

### Bio/memory tool (`author.name = "bio"`)

- **601 messages** from the `bio` tool (ChatGPT's memory feature).
- These appear as tool-role messages with `recipient: "assistant"` — they're memory recall injections.
- **Currently:** Included as regular messages but the `author.name` is lost.
- **Recommendation:** Capture `author.name` to identify bio/memory messages. These could be extracted as a separate "ChatGPT memories" collection for MattGPT's context.

### System messages and hidden messages

- **9,035 system-role messages** in total.
- **17,107 messages** have `metadata.is_visually_hidden_from_conversation: true`.
- **7,884 messages** have `weight: 0.0` (typically system/hidden messages).
- These include custom instructions, rebased system messages, and internal scaffolding.
- **Currently:** All are included in the linearised thread and embedded indiscriminately.
- **Recommendation:** Use `weight` and `is_visually_hidden_from_conversation` to either filter or de-prioritise hidden messages during embedding. System scaffolding adds noise to RAG.

---

## 6. Metadata Completeness

### Fields worth capturing for filtering/faceting

| Field | Location | Count | Use case |
|-------|----------|------:|----------|
| `default_model_slug` | Conversation | 2,098 | ✅ Already captured |
| `model_slug` | Message metadata | 48,963 | Per-message model tracking. Shows model diversity: gpt-4o (27K), gpt-5 (7.7K), gpt-4 (4.6K), etc. |
| `gizmo_id` | Conversation | 758 | Filter by custom GPT |
| `gizmo_type` | Conversation | 758 | "gpt" (10), "snorlax" (748) |
| `conversation_template_id` | Conversation | 758 | Associates conversations with GPT templates |
| `is_archived` | Conversation | 2,913 | Filter archived conversations |
| `author.name` | Message | 11,719 | Tool identification (python, dalle, browser, etc.) |
| `recipient` | Message | 79,910 | Tool dispatch target |
| `finished_duration_sec` | Message metadata | 787 | Response generation time — interesting analytics |
| `canvas.textdoc_type` | Message metadata | 854 | Canvas document type (document, code/csharp, etc.) |

### Fields safely ignored

| Field | Reason |
|-------|--------|
| `moderation_results` | Always empty |
| `safe_urls` / `blocked_urls` | Content policy; no RAG value |
| `disabled_tool_ids` | Internal config |
| `sugar_item_*` | Internal UI state |
| `is_study_mode` | No apparent RAG value |
| `plugin_ids` | Legacy (65 conversations); plugins deprecated |
| `voice` | No data (0 conversations) |
| `conversation_origin` | No data (0 conversations) |

### Tool/function call patterns observed

The export reveals a rich ecosystem of tool usage. The most common patterns:

| Pattern | Count | Description |
|---------|------:|-------------|
| `python` (Code Interpreter) | 824 calls | Code execute → result. Paired with `code` + `execution_output` content types |
| `bio` (Memory) | 602 | Memory recall injections |
| `file_search` | 5,909 results | RAG retrieval from uploaded files |
| `web` / `web.run` / `web.search` | 548+ | Web browsing. Produces `tether_quote` and `tether_browsing_display` |
| `dalle.text2im` | 328 | Image generation |
| `canmore.*` (Canvas) | 626 | Document creation/editing |
| `browser` / `myfiles_browser` | 2,142 | File/web browsing (older API) |
| `container.exec` | 13 | Container execution |
| `computer.do` | 11 | Operator tool |

---

## 7. Gap Summary and Recommendations

### Priority 1 — High impact, moderate effort

These gaps cause the most significant loss of RAG-relevant information:

#### 7.1. Handle non-text content types properly
**Gap:** `StoredMessage.From()` only reads `Content.Parts`. Content types that use `text`, `result`, `summary`, `url`, `output_str` etc. are stored with empty Parts.  
**Affected types:** `code` (1,931), `tether_quote` (5,161), `tether_browsing_display` (1,244), `execution_output` (773), `thoughts` (1,042).  
**Fix:** Add content-type-aware extraction in `StoredMessage.From()` that falls back to `text`, `result`, `summary`,  etc. when `parts` is null/empty.  
**Effort:** Small — modify one method + add properties to `Content` model.

#### 7.2. Capture file attachments
**Gap:** 7,999 file attachments with names, MIME types, sizes are completely invisible.  
**Fix:** Add `Attachment` model; parse `metadata.attachments[]`; store on `StoredMessage`. Include filenames in embedding text.  
**Effort:** Small–Medium.

#### 7.3. Parse image asset pointers in multimodal_text
**Gap:** 1,378 images in Parts become raw JSON noise.  
**Fix:** When encountering an `image_asset_pointer` in parts, generate a descriptive placeholder (e.g. `[Image: 1200×1600 uploaded image]` or `[Image: DALL-E generated]`).  
**Effort:** Small.

#### 7.4. Capture author.name and recipient
**Gap:** Tool identification information is lost.  
**Fix:** Add `AuthorName` and `Recipient` fields to `StoredMessage`. Use in embedding text for tool context.  
**Effort:** Small.

### Priority 2 — Medium impact

#### 7.5. Capture citations and content references
**Gap:** 8,651 citations and 13,106 content references lost.  
**Fix:** Add citation/reference models; parse from `metadata.citations[]` and `metadata.content_references[]`.  
**Effort:** Medium — new models, parser changes, UI could display references.

#### 7.6. Store conversation-level metadata
**Gap:** `gizmo_id`, `conversation_template_id`, `is_do_not_remember`, `memory_scope`, `is_archived` not captured.  
**Fix:** Add fields to `Conversation` model and `StoredConversation`.  
**Effort:** Small.

#### 7.7. Filter hidden/system messages
**Gap:** 17,107 hidden messages and 7,884 weight-0 messages are embedded alongside visible content, diluting RAG quality.  
**Fix:** Use `weight` and `is_visually_hidden_from_conversation` to filter or annotate messages. At minimum, exclude weight-0 messages from embedding text.  
**Effort:** Small–Medium — needs `weight` and `metadata` capture first.

#### 7.8. Capture code execution results (aggregate_result)
**Gap:** 773 code execution results (status, code, output) lost.  
**Fix:** Parse `metadata.aggregate_result` for code interpreter tool messages.  
**Effort:** Small.

### Priority 3 — Low impact or low count

#### 7.9. Extract user profile from `user_editable_context`
**Gap:** Custom instructions present in 1,170 messages but not separately extracted.  
**Fix:** Extract the most recent `user_profile` and `user_instructions` and store as a profile document.  
**Effort:** Small.

#### 7.10. Canvas document awareness
**Gap:** 854 canvas documents (types: document, code/csharp, code/python, etc.) not tracked.  
**Fix:** Extract `metadata.canvas` info (textdoc_type, title) and store as message annotation.  
**Effort:** Small.

#### 7.11. `reasoning_recap`, `system_error`, `computer_output`, `citable_code_output`
**Gap:** Small counts (13–788), low RAG value.  
**Fix:** Add fallback handling for non-parts content fields.  
**Effort:** Small — included in 7.1 fix.

---

## Appendix A: Current parser data flow

```
conversations.json
  → ConversationParser.ParseAsync() — streams JSON; deserialises Conversation model
    → Linearise() — walks parent pointers from current_node to build active thread
  → ParsedConversation { Id, Title, CreateTime, UpdateTime, DefaultModelSlug, Messages[] }
    → StoredConversation.From() — maps to MongoDB document
      → StoredMessage.From() — reads message.Content.Parts only; skips metadata
```

**Key bottleneck:** `StoredMessage.From()` is where most information is lost. It reads only:
- `message.Id`
- `message.Author.Role`
- `message.Content.ContentType`
- `message.Content.Parts` (converted to strings via JsonElement)
- `message.CreateTime`

Everything else — `author.name`, `recipient`, `weight`, `metadata.*`, non-parts content fields — is discarded.

## Appendix B: Conversation model field coverage

The `ChatGptExport.cs` `Conversation` class only has 8 properties:
- `Id`, `Title`, `CreateTime`, `UpdateTime`, `Mapping`, `CurrentNode`, `DefaultModelSlug`

The schema has 25+ conversation-level fields. The most valuable uncaptured ones are: `gizmo_id`, `conversation_template_id`, `is_do_not_remember`, `memory_scope`.

The `Message` class only has 4 properties:
- `Id`, `Author`, `Content`, `CreateTime`

The schema has 8+ message-level fields. The most valuable uncaptured ones are: `recipient`, `weight`, `metadata` (the richest source of structured data).

The `Content` class only has 3 properties:
- `ContentType`, `Parts`, `Text`

The schema has 17+ content-level fields. The most valuable uncaptured ones (by content type): `url`, `domain`, `title` (tether_quote); `result`, `summary` (tether_browsing_display); `language` (code); `user_profile`, `user_instructions` (user_editable_context); `thoughts` (thoughts).
