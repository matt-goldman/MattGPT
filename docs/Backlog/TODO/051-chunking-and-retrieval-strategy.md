# 051 — Chunking and Retrieval Strategy: Reconcile Chunking with Reranking

**Status:** TODO
**Sequence:** 50
**Dependencies:** 009 (embeddings), 047 (reranking), 048 (digest on import — fixes digest coverage)
**Decisions:** [ADR-014](../Decisions/014-the-conversation-is-the-unit-of-retrieval.md)

---

## Summary

Implement ADR-016. Whole-conversation chunks and a conversation-level reranker currently discriminate on the same signal, and reranking's narrow cut-off has removed the cross-conversation breadth that produced the best results observed so far. This issue resolves that conflict structurally, makes chunking strategy configurable and benchmarkable alongside the existing provider and store configuration, and builds the evidence infrastructure that every remaining chunking parameter depends on.

The architectural decisions come from the ADR and are not benchmarked. The parameter choices are benchmarked, in a fixed order, and cannot begin until Phase 0 is complete.

## Background

### The conflict

Chunk size was moved to whole-conversation during retrieval accuracy work, and appeared to improve results substantially. Reranking (047) was not working at the time and has since been fixed. Neither was revisited in light of the other:

- Whole-conversation chunks make the chunk and the conversation the same object, so a conversation-level reranker has nothing left to separate.
- Reranking then narrows to the best-matching conversation, so context no longer spans conversations, and answers to multi-conversation questions have become thinner — missing details that the pre-reranking configuration captured.

ADR-014's resolution: the conversation is the unit of *retrieval, ranking and citation*, but not of *answer composition*. Chunks become small, discriminative evidence; scores pool to the conversation by maximum; the reranker orders conversations and retains several; generation receives the retained conversations' records plus their matching exchanges.

### Why no measurement exists yet

The apparent chunk-size gain is confounded three ways, all pointing the same direction: store and embedding model were not held fixed across runs; digest coverage was inconsistent because digests were never generated on import (048); and the digest forms part of the embedding text, so conversations with digests received augmented embeddings and those without received bare ones.

Separately, the one result that demonstrated good cross-conversation synthesis has no record of its sources — results were displayed but not persisted at the time. That fix has since landed, but the result itself is unreproducible and cannot serve as a baseline.

Consequence: there is currently no usable baseline, and no parameter in this issue can be decided by comparison against the corpus in its present state. Phase 0 exists to fix that and is a hard gate on Phases 2 and 3.

### Testbed requirement

MattGPT's purpose includes being a testbed for techniques that port to other projects. Chunking strategy should therefore be configurable in the same way model providers, embedding models and persistence engines already are — selectable, with results comparable across configurations — rather than a hardcoded choice. Several of the parameters below are expected to resolve differently per domain, so the configuration surface is part of the deliverable, not an afterthought.

## Requirements

### Phase 0 — Evidence infrastructure (gates Phases 2 and 3)

1. **Eval set** of 20–30 questions with known answers over known conversations, stored in the repository and runnable as a harness. It must include:
   - questions whose answers are known to span several conversations — without these, the breadth regression this issue exists to fix is invisible to scoring;
   - **unanswerable questions**, whose correct outcome is abstention — without these, every change below scores as an improvement, since all of them trade recall against precision;
   - at least one conversation containing a decision reversed later in the same conversation, as a drift probe;
   - at least one query carrying a false premise that the answer must correct from source (the existing power-brick example is a known-good case of this shape).
2. **Scoring must report recall and precision separately**, plus abstention correctness on the unanswerable set. A single blended score will hide the trade every parameter makes.
3. **Fixed configuration per run.** One store, one embedding model, recorded with the run. Scores are not comparable across stores; score semantics differ by implementation.
4. **Confirm score semantics for the configured store** before using any score as a metric or threshold. Qdrant returns raw cosine by default, so values at or near 1.0 need explaining rather than accepting. Record what each store's score actually is.
5. **Digest provenance** recorded on the conversation: whether the digest was derived from messages or from the record. `GenerateSummaryAsync` prefers the record when one exists, so provenance currently depends on the order the user clicked things, and an eval run would silently measure a mixture of two pipelines.
6. **Verify source persistence** captures everything needed to reproduce a result: which conversations were retrieved, their scores pre- and post-rerank, and which were retained for generation.

### Phase 1 — Architecture (derived from ADR-014, not benchmarked)

7. **Pool chunk scores to the conversation by maximum.** Not sum — sum rewards length, and this corpus has a wide length distribution in which the longest conversations are substantive working sessions that would dominate unrelated queries.
8. **Rerank at conversation level only.** Remove chunk-level reranking where it exists. The candidate document for a conversation is, in order of availability: its record, its digest, or a stitched window of its matching chunks.
9. **Retained breadth after reranking is an explicit configuration value**, not an artifact of top-k. Implement as a count cap plus a relative score threshold, so a query with one clearly dominant conversation does not pad context with weak matches. Default to retaining several, not one.
10. **Generation receives, per retained conversation, its record (where one exists) plus its matching chunks.** Record alone loses the citation anchor; chunks alone lose the resolved context.
11. **Chunking strategy is a named, configurable strategy** with at minimum: whole-conversation (current), message, and exchange. Selectable per deployment, recorded with every embedding run so a conversation's vectors can be traced to the strategy that produced them.
12. **Changing strategy requires a full re-embed.** Provide the path and make the current strategy visible per conversation, so a corpus embedded under mixed strategies is detectable rather than silently inconsistent.

### Phase 2 — Read-time neighbour expansion (gates the overlap decision)

13. **Expand a retrieved chunk to its neighbours at read time** before passing it to generation, configurable by window size.
14. Zero overlap is only defensible if this exists. ADR-014 records zero overlap as **conditional on this mechanism**; without it, nothing covers a thought spanning several turns, and overlap must be reconsidered instead.
15. Expansion must not duplicate content when two retained chunks from the same conversation have overlapping windows.

### Phase 3 — Measurement (requires Phase 0; strictly ordered)

Measure in this order. The space is multiplicative, and later variables are cheaper to re-run than earlier ones.

16. **Embedding model first.** Probably the largest single lever and the cheapest to swap, and the variable that invalidated the earlier comparison. Measuring it after chunk unit would attribute its gains to chunking — the mistake already made once.
17. **Chunk unit**, with pooling fixed: whole-conversation vs message vs exchange.
18. **Retained breadth**, including measuring what proportion of eval questions are genuinely multi-conversation. That proportion is currently assumed high on the basis of journal-style usage and is unmeasured.
19. **Augmentation of chunk embedding text:** none vs digest prefix vs per-chunk situating line generated at embed time. Expect the digest prefix to improve conversation-level recall and reduce intra-conversation discrimination; score precision separately so the blunting is visible.
20. **Overlap last**, expecting zero given Phase 2.
21. **Embedding-text weighting between user and assistant turns.** Assistant turns dominate by length while queries are phrased like user turns. Cheap to test once the harness exists; no provisional answer.

### Definitions

22. **Exchange** = a user turn plus the assistant turns that follow it, up to the next user turn. Justified by interpretability: a user turn alone is frequently uninterpretable, and pairing it with the response makes the chunk self-describing. **No claim is made that an exchange contains a complete decision** — decision boundaries are not knowable before resolution, which is why the claim pipeline resolves relations in a later pass over the whole set rather than at chunk boundaries.
23. Where a chunk exceeds the configured size and must be split, split points carry addresses, using the same address model as the claim pipeline (source, ordinal, parent ordinal, actor, timestamp). Not blocking, but the two should not invent separate schemes.

## Acceptance Criteria

- [ ] The eval harness runs against a named configuration and reports recall, precision and abstention correctness separately.
- [ ] The eval set includes multi-conversation, unanswerable, late-reversal and false-premise questions.
- [ ] Each store's score semantics are documented, and the configured store's scores are explained rather than assumed.
- [ ] Digest provenance is recorded and queryable; an eval run can be restricted to one provenance.
- [ ] A retrieval result can be fully reproduced from persisted data: retrieved conversations, pre- and post-rerank scores, and what was retained.
- [ ] Chunk scores pool to conversation by maximum; no chunk-level reranking remains.
- [ ] Retained breadth is configurable as count plus relative threshold, and a multi-conversation eval question is answered using several conversations.
- [ ] Generation context contains, per retained conversation, its record where one exists plus its matching chunks.
- [ ] Chunking strategy is selectable, and every conversation records the strategy its vectors were built under.
- [ ] A corpus embedded under mixed strategies is detectable, and a full re-embed under one strategy is available.
- [ ] Read-time neighbour expansion works, is window-configurable, and does not duplicate content across overlapping windows.
- [ ] Each Phase 3 measurement is recorded with its configuration and its result, including the measurements that changed nothing.
- [ ] The previously-good cross-conversation query (power brick / handheld) is in the eval set and scores at least as well as the pre-reranking configuration did.

## Non-goals

- **The claim layer.** ADR-016 makes it the cross-conversation synthesis substrate, but it is its own body of work with its own representation decisions. This issue must not make retrieval depend on it.
- **Graph RAG.** A graph over claims is a later view on the claim table, not part of this issue.
- **Changing the digest or record prompts.** The digest is doing its job; summarisation quality is the other thread.
- **Intra-conversation ranking at scale.** Matching chunks are carried to generation, which handles a handful. A conversation with dozens of matches is a known limit, not a target here.

## Notes

- Phase 0 is separable and could ship as its own item. It has its own acceptance criteria and value independent of the chunking work, and nothing in Phases 2–3 can be trusted without it.
- Phase 1 is derived from the ADR and should not be gated on Phase 0. It fixes a known regression; waiting for measurement to confirm a conclusion already reasoned through just extends the regression.
- Record Phase 3 results where the ADR's provisional answers can be updated with a basis of *measured*. The ADR's deferred table is the intended home.
- Expect at least one Phase 3 result to contradict the ADR's provisional answer. That is the point of running it; the ADR's confidence is explicitly low on pooling and on augmentation.
