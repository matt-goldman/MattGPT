# Pipeline (import / embedding / status)

## 2026-10-04 — Undeserializable conversation rows are invisible, wasteful, and can starve the embedder

**Context:** Improving the re-run-embeddings UX; a fresh import + immediate re-run was
producing `JsonException`s when reading conversations back out of Postgres (same build wrote
and read them, so not model drift).

**Observation — three linked facts:**
1. `GetStatusCountsAsync` counts via the promoted `processing_status` **column**
   (`SELECT processing_status, COUNT(*) ... GROUP BY`), no deserialization. The embedder fetches
   via `GetByStatusesAsync`, which deserializes JSONB and **silently drops** any row that fails
   (`ReadConversationsAsync`). So a bad row shows forever in the counts (e.g. as `Imported`) but
   is never embedded and never counted as an error — an invisible gap between the two numbers.
2. Dropped rows are never added to the embedder's `attempted` set and never get a status update,
   so they stay in the eligible set (`{Imported, Summarised, EmbeddingError}`) and are
   re-fetched + re-failed + re-logged on **every** run. Permanent waste + log spam.
3. **Starvation risk:** `GetByStatusesAsync` is `LIMIT 50` with **no `ORDER BY`**. The embed loop
   (`EmbeddingService.EmbedAsync`) breaks when a fetched batch *deserializes* to 0 items. If a
   50-row batch comes back all-bad, it deserializes to empty → loop breaks → valid `Imported`
   rows behind them are never embedded.

**Root cause (found 2026-10-04):** a write/read enum-serialization asymmetry in the Postgres
repo. `UpsertAsync` serialises the whole `StoredConversation` with converter-less
`SerializerOptions`, so `ProcessingStatus` is written as a **number** (`"processingStatus": 0`).
But `UpdateSummaryAsync` / `UpdateEmbeddingAsync` overwrite that jsonb field via
`jsonb_set(..., '{processingStatus}', $4::jsonb)` with `$4 = JsonSerializer.Serialize(status.ToString())`
— a **string** (`"Embedded"`). Default STJ can read the number but throws on the string → every
row that has been through an update becomes unreadable. ("Small subset" = only updated rows;
freshly-imported-but-never-updated rows still hold the numeric form and read fine.) Not model
drift, not corruption, not metadata pollution (only `StoredConversation` is ever written to the
`conversations` table; the user profile goes to its own table).

**Fix applied:** put `[JsonConverter(typeof(JsonStringEnumConverter))]` on the
`ConversationProcessingStatus` enum itself (`StoredConversation.cs`), not in the repo's options —
so every System.Text.Json consumer inherits the string contract rather than each backend
configuring enum handling. Reads accept both the numeric (legacy/insert) and string (update)
forms, so existing bad rows become readable again; new upserts emit the string form, matching
`jsonb_set`. The `JsonException.Path` logging in `ReadConversationsAsync` remains as a safety net
for any future deserialization drift.
**MongoDB:** uses the BSON serializer (`BsonSerializer.Deserialize<StoredConversation>`), which
the STJ attribute does not touch, so its enum handling is a separate, higher-layer concern — left
alone deliberately (Postgres is the active backend).

**Pointer:** `src/API/MattGPT.ApiService/Services/EmbeddingService.cs:83-115` (loop + `attempted`),
`src/Infrastructure/PostgresModule/Services/PostgresConversationRepository.cs`
(`ReadConversationsAsync`, `GetByStatusesAsync` ~114-135, `GetStatusCountsAsync` ~252)
**Tags:** code-path, gotcha, pipeline, embedding, postgres

## 2026-10-04 — A 400 from the embedding provider hides its reason in the raw response body, not the exception message

**Context:** Diagnosing frequent 400s from `embeddingGenerator.GenerateAsync` in `EmbeddingService`.

**Observation:** The OpenAI path (`OpenAIModule`) uses the System.ClientModel SDK, which throws
`ClientResultException` on a 4xx. Its `.Message` is only `"Service request failed. Status: 400
(Bad Request)"` — the server's actual error JSON (unknown model, input-too-long, bad dimension,
invalid key) lives in `GetRawResponse().Content`, which the plain `logger.LogWarning(ex, ...)`
never surfaced. Azure.Core's `RequestFailedException` has the same `GetRawResponse().Content`
shape. A 400 is correctly non-transient (`IsRetryableStatus`), so it skips retry and lands
straight in `EmbedConversationAsync`'s catch.

**Fix applied:** added `TryGetServerErrorBody` (reflective `GetRawResponse()` → `Content.ToString()`,
truncated to 2 000 chars) and `GetHttpStatusCode` (chain-walking), plus `LogEmbeddingFailure`
which also logs provider/endpoint/model from `IEmbeddingGenerator.GetService(typeof(
EmbeddingGeneratorMetadata))` — `ProviderUri` is the destination URL, provider-agnostic, no SDK
reference needed. Input **length** is logged, never the conversation text (PII).

**Gotcha for diagnosis:** `EmbeddingGeneratorMetadata.ProviderUri` is the configured **base**
endpoint (e.g. `https://api.openai.com/v1/`), not the exact `/embeddings` path. For the OpenAI
module note `Module.cs` appends `/v1/` to `LLM:Endpoint` — a double `/v1` in the base endpoint
config is a classic 400/404 cause worth checking first.

**Pointer:** `src/API/MattGPT.ApiService/Services/EmbeddingService.cs` (`TryGetServerErrorBody`,
`GetHttpStatusCode`, `LogEmbeddingFailure`), `src/Infrastructure/OpenAIModule/Module.cs:25` (uri build)
**Tags:** code-path, gotcha, pipeline, embedding, diagnostics, openai

## 2026-10-06 — Digest state vs processing status (048): what each value now means

**Context:** Implementing 048 (digest/record split, opt-in import digests).
**Observation:** `ProcessingStatus` is now purely "is the vector current?". `Summarised` means "has a digest that is newer than its embedding", which is what `CountDigestsAwaitingEmbeddingAsync` counts, and embed runs pick it up. Digest outcome lives in `SummaryStatus` (`None`/`Generated`/`Skipped`/`Failed`). Failures and skips pass `status: null` to `UpdateSummaryAsync` so they never touch `ProcessingStatus`. `SummaryError` is legacy-only; it's in `EmbeddableStatuses` so old rows finally embed. The bulk run selects on `Summary == null && SummaryStatus != Skipped` (`GetUnsummarisedAsync`), not on status. Chat projections are excluded and owned by 049's pipeline.
**Gotchas:**
- `ImportJob.Status = Complete` is set *before* the digest and embed phases. Wait on `CompletedAt` (set in `finally`) for "job fully done"; tests that wait on `Status` race the later phases.
- The test `FakeConversationRepository.UpsertAsync` now also stores the document (it used to only record it), so code that upserts and then reads back (import → digest by id) works in tests.
- Re-import replaces the whole document, wiping `Summary`/`SummaryStatus`/`Record` (logged as a follow-up in 048).
**Pointer:** `src/API/MattGPT.ApiService/Services/SummarisationService.cs` (`SummariseConversationAsync`, `BuildTranscript`), `ImportProcessingService.TrySummariseImportedAsync`, `SummaryProcessingService.cs` (queue + dedup)
**Tags:** code-path, gotcha, pipeline, summarisation

## 2026-10-07 — Weight/hidden filtering does NOT exclude tool traffic; role is the only thing that does

**Context:** Investigating context blowouts / poor retrieval (defect 051). Hypothesis was that
ChatGPT export tool results (uploaded files, web fetches, python output) were being embedded.

**Observation — the arithmetic that settles it.** Four read paths filtered with the hand-copied
predicate `!m.IsHidden && m.Weight != 0.0`, which *looks* like it handles scaffolding. It does not
touch tool traffic. From `docs/TechnicalReference/export-analysis-raw.md`: 79,910 messages total,
of which `weight: 0.0` = **7,884**, but `role: tool` = **11,719** and `role: system` = **9,035**
(20,754 combined). So most tool/system messages carry `weight: 1.0` and no hidden flag and sail
straight through. Don't trust the `is_visually_hidden_from_conversation` count (17,107) as a
counter-argument — that figure is *key presence*, counted by `tools/analyse-export.py`, not
`== true`, and the doc's prose overstates it.

**Empirical confirmation, which is cheaper than the arithmetic:** `GET /conversations/{id}` already
applied the weight/hidden filter, and the conversation view was still unreadable. Anything visible
there is by definition passing that filter.

**The real trap:** `StoredConversationExtensions.ToExcerpt()` applied **no filter at all** — and it
is the method that renders a retrieved conversation into the model prompt (`RagService.BuildMessages`
~line 740) and into `search_memories` results (`SearchMemoriesTool` ~line 141). So the path with the
most direct effect on tokens and answer quality was the unfiltered one, and it disagreed with
`ToEmbeddingText`: conversations were indexed on dialogue, then served to the model as dialogue plus
every file they ever touched. When auditing "what reaches the model", check `ToExcerpt`, not just
`ToEmbeddingText` — they are adjacent in the same file and easy to conflate.

**Fix shape:** one predicate, `StoredMessageFilters.IsConversational` /
`StoredConversation.ConversationalMessages` in Contracts, used by all five read paths (embedding,
excerpt, digest transcript, conversation endpoint, `ConversationTextSnippets.Build`). Deliberately
**extension members**, not an instance property: `StoredMessage` is serialised to Postgres JSONB and
Mongo BSON, and a get-only property would widen that contract for no reason (see the enum
serialisation nugget above for why that contract is load-bearing).

**Gotchas:**
- `Role` is a bare `string` with no validation anywhere; the schema pins it to
  `system|user|assistant|tool` but nothing enforces it.
- `author.name` (which tool produced a result) and `recipient` (which tool was called) are **not**
  on `StoredMessage`. `author.name` is parsed into `Message` but dropped in `StoredMessage.From`;
  `recipient` isn't in the model at all. Issue 024 covers both, and issue 052 is blocked on it.
- **Assistant tool *calls* are still unfiltered** — `role: assistant` + `recipient: python`/`bio` is
  scaffolding but indistinguishable until `recipient` is captured.
- Issue 024 requirement 5 ("prefix tool messages with the tool name in embedding text") would
  reintroduce this defect; 024 has been annotated, but check that annotation survived if 024 is
  picked up.
- Changing `ToEmbeddingText` invalidates existing vectors — `POST /conversations/embed` to rebuild.
  Pre-existing digests also summarised tool output.
- Not every non-dialogue role is a clear cut: `thoughts` / `reasoning_recap` content types are
  `assistant`-role reasoning traces and are still included, deliberately undecided.

**Pointer:** `src/Common/Contracts/Models/StoredMessageFilters.cs` (the predicate),
`src/API/MattGPT.ApiService/Extensions/StoredConversationExtensions.cs` (both render paths),
`docs/TechnicalReference/export-analysis-raw.md` (role/weight/author-name counts),
`docs/Backlog/Done/051-defect-tool-results-pollute-embeddings-and-ui.md`
**Tags:** code-path, gotcha, pipeline, embedding, summarisation, retrieval, import
