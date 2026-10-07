## ADR-016: The conversation is the unit of retrieval, not of answer composition

**Date:** 2026-10-07
**Status:** Accepted
**Related Issues:** 047 (reranking), 048 (record export, digest on import), 009 (embeddings)
**Related Decisions:** [ADR-013](013-chat-sessions-as-conversation-projections.md)

### Context

Chunk size was changed from per-message to whole-conversation during work on retrieval accuracy, and the change appeared substantial: queries that previously returned nothing useful began returning the right conversation, including queries carrying a false premise that the answer then corrected from source. Reranking was added separately (047) and was not working at the time of that change. It works now, and the two have not been reconciled — whole-conversation chunks and a conversation-level reranker are the same discrimination applied twice, so the second stage has nothing left to separate.

Retrieval quality has since regressed in a specific way: answers now miss details that span conversations. Before reranking, roughly ten conversations reached the model and it synthesised across them. With reranking narrowing to the best-matching conversation, the context no longer spans, and the answers are correspondingly thinner. The earlier good result cannot be used as a baseline, because the conversations that produced it were displayed but not persisted at the time (now fixed) — so which conversations contributed to it is unrecoverable.

The measured improvement attributed to chunk size is also confounded, in at least three ways, all pointing the same direction:

- **Store and embedding model were not held fixed** across runs. Score semantics differ by store (Weaviate computes similarity locally; Qdrant, pgVector, Azure AI Search and Pinecone each return their own score), so scores are not comparable across configurations either.
- **Digest coverage was inconsistent.** Digests were never generated on import (see 048), so some conversations had them and some did not, depending on which manual jobs had been run.
- **The digest is part of the embedding text** (title + summary + message content, per ADR-013). Combined with the previous point, conversations with digests received augmented embeddings and conversations without received bare ones.

Each of these plausibly accounts for part of the gain. No conclusion about chunk size can be drawn from the corpus in its present state.

Underneath all of it are two questions that were never asked explicitly, and on which every chunking parameter depends: **what object is retrieved, and what object is the answer composed from?** They are not the same object, and conflating them is what produced both the duplicated discrimination stage and the regression above.

### Decision

**The conversation is the unit of retrieval: of candidacy, of ranking, and of citation. It is not the unit of answer composition.** Answers are composed across conversations, so retrieval must deliver a set.

In MattGPT, retrieval surfaces conversations, links them as sources, and the model answers from them. Nothing in the product consumes an isolated message, and the citation and cross-check behaviour — following the link to verify the answer against the original — is the core value. That is conversation-shaped. But the corpus is a journal: a single project's history is spread across many conversations by construction, so a question about that project is a multi-conversation question by default rather than by exception.

From that:

1. **Chunks are evidence of conversation relevance, not retrieval results.** They are small and discriminative, and exist to be matched against. They are not the thing returned.
2. **Chunk scores pool to the conversation by maximum, not sum.** Sum-pooling rewards length, which in this corpus means the longest working sessions dominate every query regardless of relevance.
3. **The reranker scores conversations, not chunks, and orders a set rather than selecting a winner.** This removes the duplicated discrimination stage. The candidate document for a conversation is, in order of preference: its record, its digest, or a stitched window of its matching exchanges.
4. **Retained breadth after reranking is an explicit, configurable parameter, not a side effect of top-k.** Cutting to a single conversation is a regression for any question whose answer spans conversations, and the default must retain several. What proportion of real queries are multi-conversation is an open empirical question (see Deferred).
5. **Generation receives the retained conversations' records (where they exist) plus their specific matching exchanges.** The records give resolved context; the exchanges give verbatim grounding for the citation.
6. **Claims are the cross-conversation synthesis layer, and are not an embeddable unit.** A claim is a derived assertion; retrieving one would make the citation point at a model's paraphrase rather than at the conversation. But the claim index is the only representation in which a decision made in one conversation and reversed in another are adjacent, so it is what cross-conversation questions are answered *through* — queried and filtered to assemble the relevant set, with conversations remaining the cited artifacts.
7. **The chunk unit is the exchange, justified by interpretability, not by semantic completeness.** A user turn alone is frequently uninterpretable; pairing it with the following assistant turn makes the chunk self-describing. No claim is made that an exchange contains a complete decision — decision boundaries are not knowable before resolution, which is why the claim pipeline resolves relations in a later pass over the whole set rather than at chunk boundaries.

Point 3 supersedes an earlier framing in which the record would be supplied as a per-chunk parent document. The record is identical for every chunk from the same conversation, so used that way the reranker degenerates into conversation-level reranking anyway, but implicitly and with the pooling left undefined. Pool explicitly, then rerank.

### The Good

- **The reranking conflict resolves structurally rather than by tuning.** With pooling at conversation level, chunks are free to be small and discriminative again, which is the regime reranking was built for. The two stages stop competing for the same signal.
- **The current regression has a named cause and a named fix.** It is a breadth cut-off, not a reranking defect, and point 4 addresses it directly.
- **Chunk size stops being a retrieval-quality lever and becomes a discrimination lever.** It affects which conversations are found, not what is returned, so its tuning range widens considerably and a bad choice degrades ranking rather than breaking answers.
- **Citation integrity is preserved by construction.** Every cited artifact is something the user wrote or received, never a generated intermediate, which is what makes the verify-the-answer workflow trustworthy.
- **ADR-013 needs no amendment.** Projections are conversations, so sessions inherit this decision with no session-specific retrieval code, exactly as that ADR intended.
- **The claim index now has a load-bearing job.** It was previously justified as a convenience for filtering; under point 6 it is the mechanism for a class of question the vector path cannot answer at all.

### The Bad

**Retaining breadth costs precision and context budget, and the right breadth is unknown.**
Several conversations in context means more irrelevant material reaching the model, which is the cost reranking was adopted to avoid. *Mitigation:* make breadth configurable and measure it rather than guessing; use a relative score threshold alongside a count cap, so a query with one clearly dominant conversation doesn't pad the context with weak matches. *Justification:* a thin answer from one conversation is a worse failure than a slightly noisy answer from four, because the user cannot tell that something is missing.

**Zero overlap is now a conditional bet, not a default.**
With the semantic-completeness justification withdrawn, nothing guarantees that a thought spanning several turns survives chunking. The only thing covering it is read-time neighbour expansion. *Mitigation:* treat read-time expansion as a prerequisite for zero overlap rather than an optimisation; if it is not built, overlap must be reconsidered. *Residual risk:* accepted and unmitigated for now, because duplicated vectors consume top-k and would mask the breadth problem above.

**Max-pooling discards corroboration.**
A conversation that matches a query weakly in eight places scores the same as one matching weakly in one place. For a journal, repeated weak matches across a long working session are plausibly a real relevance signal. *Justification:* sum-pooling's length bias is a more damaging failure in this corpus, and max-pooling is the conservative default. *Mitigation:* a count-aware tiebreak (match count as a secondary sort within a score band) is cheap to add if evaluation shows corroboration matters.

**Conversation-level reranking cannot locate within a conversation.**
Once the conversation is the rerank unit, nothing ranks exchanges inside it. "Where in this conversation was X decided" has no mechanism in the retrieval path. *Mitigation:* the matching exchanges are already carried through to generation (point 5), so locating is handled at generation and citation time rather than by ranking. *Honest limit:* this works for a handful of exchanges and will not scale to a conversation with dozens of matches.

**The claim index introduces a derived layer that can be wrong, and the error is invisible.**
A hallucinated or mis-resolved claim steers retrieval toward or away from conversations without appearing in the cited output, so the user sees a plausible answer with correct-looking citations and no indication that the selection was driven by a bad claim. *Mitigation:* every claim carries its address, so any claim can be checked against source; resolution runs as a separate pass over the full claim set rather than inline, so conflicts are detectable; relations are never inferred at a stage that cannot observe both ends. *Residual risk:* real. The claim layer must stay auditable, and must not become the only path to a conversation — the vector path remains independent.

**No usable baseline exists, and the most convincing evidence so far is unreproducible.**
The confounders above invalidate cross-run comparison, and the one result that demonstrated good cross-conversation synthesis has no record of its sources. Everything in Deferred is therefore being decided on reasoning rather than measurement. *Mitigation:* source persistence is now in place, so future results are reproducible; the eval set is a prerequisite for the chunking work rather than a parallel activity.

**Augmentation will look better than it is.**
Prepending the digest to each chunk pulls every chunk in a conversation toward a shared centroid: conversation-level recall rises, intra-conversation discrimination falls. Under this ADR that is the favoured direction, which means the metric this ADR cares about will improve even where retrieval has genuinely got blunter. *Mitigation:* score precision separately from recall, and include unanswerable questions so blunting shows up as false positives.

**Two conversations with identical content can hold digests derived from different sources.**
`GenerateSummaryAsync` prefers the record when one exists, and nothing re-digests after a record is generated, so the digest's provenance depends on the order the user clicked things. *Mitigation:* record digest provenance on the conversation; without it the eval set is silently measuring a mixture of two pipelines.

### Deliberately deferred

These are empirical and belong to the chunking work, which this ADR is a prerequisite for. They are listed with provisional answers so the questions are resumable rather than merely open.

| Question | Default pending evidence | What would overturn it |
|---|---|---|
| Retained conversations after reranking | Several, not one; count cap plus relative score threshold | Measured proportion of multi-conversation queries |
| Proportion of queries that are multi-conversation | Unknown; assumed high given journal usage | Eval set scoring, answerable vs multi-source |
| Chunk unit | Exchange (user turn + following assistant turn) | Evidence that assistant-turn length swamps user-phrased queries |
| Chunk size where a unit must be split | Unresolved; split points to use the claim pipeline's address model | — |
| Overlap | Zero, **conditional on read-time neighbour expansion existing** | Expansion not built, or a spanning-thought failure case |
| Pooling | Max | Evidence that match count carries relevance signal |
| Augmentation | Digest prefix; per-chunk situating line as the stronger variant | Precision loss exceeding recall gain |
| Embedding-text weighting, user vs assistant turns | Unresolved | — |

Required ordering, because the parameter space is multiplicative and has no clean baseline: build the eval set, fix store and embedding model, measure the embedding model first (largest and cheapest lever, and the variable that invalidated the earlier comparison), then chunk unit with pooling fixed, then breadth, then augmentation, then overlap last.

The eval set must include unanswerable questions, and must include questions whose answers are known to span several conversations. Without the first, every change reports as an improvement; without the second, the regression this ADR exists to fix is invisible to scoring.

### Generalisation

The reasoning is intended to be portable; the conclusion is not. The portable part is the ordering: **identify the retrieved object and the answer object separately, then derive chunking, pooling, reranking breadth and generation from both.** Conflating them is the error this ADR corrects, and it is the error most likely to recur in a new domain, because the retrieved object is usually obvious and the answer object usually isn't.

The Bad section is where the generalisation does its real work. Each entry is a question to re-ask in a new domain, and the answers diverge even where the structure holds:

- *Breadth cost* — depends on whether answers span documents. A support knowledge base: usually not. A journal, a meeting corpus, a case file: usually yes.
- *Overlap bet* — depends on whether natural boundaries exist. Conversations and transcripts have them; prose documents largely don't, and there overlap is not optional.
- *Pooling choice* — depends on the length distribution. Uniform-length documents make the max/sum distinction nearly moot.
- *Locating within a unit* — depends on unit size. Matters for long working sessions, not for short records.
- *Derived-layer risk* — depends on whether the derived layer is cited or only consulted. Citing it makes errors visible and auditable; consulting it silently, as here, does not.

Known divergence already identified: in the adaptive-learning work the retrievable object is a concept, not a document. There, concepts and claims probably *are* embedded and cited, point 6 inverts, and the derived-layer risk changes character because the derived layer becomes visible to the user. For meeting transcripts the retrieved object is likely the meeting, making it structurally close to this case — but with open-ended entities and no message tree, so the address model degrades to ordinal plus actor plus time, and entity resolution becomes the hard problem rather than a solved one.

### Alternatives Considered

- **Chunk-level retrieval, returning passages rather than conversations.** Rejected: it would require a citation story for a fragment, and the fragment is not what the user verifies against. It is the right choice for corpora where the passage is self-contained (documentation, reference material), and not for working conversations.
- **Keep whole-conversation chunks and drop reranking.** Workable and simpler, and close to the configuration that produced the best observed result. Rejected because it gives up intra-conversation locating entirely and discards a capability the other projects need — but noted that it is the current best-evidenced configuration, and that rejecting it is a reasoned bet rather than a measured one.
- **Keep whole-conversation chunks and rerank at chunk level.** Rejected as the status quo under examination: the two stages discriminate on the same signal, so the second adds latency and cost without adding information.
- **Sum-pooling chunk scores to conversation level.** Rejected: it rewards conversation length, and this corpus has a wide length distribution with the longest conversations being substantive working sessions that would then dominate unrelated queries.
- **Rerank to a single best conversation and rely on the claim index for cross-conversation questions.** Rejected for now: it makes the claim index load-bearing for correctness before it exists or has been validated, and leaves a window in which multi-conversation questions are answered badly. Worth revisiting once the claim layer is built and audited.
