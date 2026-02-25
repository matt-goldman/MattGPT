# Conversation Memory Restoration via RAG

## Goal

Restore a large ChatGPT conversation export (~148MB, ~2,213 conversations) as usable "memory" for any LLM, using a retrieval-augmented generation (RAG) pipeline.

## Source Data

- **File**: `conversations.json` — exported from OpenAI/ChatGPT
- **Schema**: `conversations.schema.json` — full JSON Schema documenting the structure
- **Structure**: Array of conversations, each containing a tree of messages (user, assistant, system, tool) with rich metadata, attachments, citations, code execution results, and more

## Process

### 1. Stream & Chunk

Stream the JSON file and break it into chunks. The natural chunk boundary is a single **conversation** — each is a self-contained thread with a title, timestamps, model info, and a tree of messages.

Within each conversation, the message tree needs to be **linearised** by walking from the `current_node` back through parent pointers to the root, then reversing. This gives the active thread as a flat sequence of turns.

### 2. Store Full Chunks in Document DB

Store each chunk (full conversation data) in a document database (e.g. MongoDB). This preserves the complete original content — message text, metadata, attachments, code blocks, tool outputs, citations — so nothing is lost.

Each document gets a stable ID (the conversation's `conversation_id` or `id` field works well).

### 3. Generate Summaries with an LLM

For each stored conversation, use an LLM to generate a summary. The summary should capture:

- What the conversation was about (topic, project, problem)
- Key decisions, conclusions, or outputs
- Notable context (which model was used, whether code was executed, files attached, images generated, etc.)

### 4. Generate Embeddings

Run the summaries through an embeddings model to produce vector representations. Store each embedding alongside:

- The summary text
- A pointer (ID / reference) back to the full conversation document in the document DB
- Key metadata for filtering (timestamps, model used, conversation title, tags/topics)

Store these in a vector database (or a vector-capable index within Mongo).

### 5. Use as RAG Memory

When interacting with an LLM:

1. **Query** the vector store with the current prompt/context to find relevant past conversations
2. **Retrieve** the matching summaries (and optionally the full chunks from the document DB for deeper context)
3. **Inject** the retrieved context into the LLM's prompt as restored "memory"

This gives the LLM access to the history, preferences, decisions, and knowledge from the original conversations — without needing to fit 148MB into a context window.
