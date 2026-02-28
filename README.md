# <img src="assets/logo.png" alt="MattGPT logo" height="48" align="center"> MattGPT

A .NET Aspire application that imports your entire ChatGPT conversation history and makes it available as RAG (Retrieval-Augmented Generation) memory for any LLM.

## Screenshots

<table>
  <tr>
    <td><img src="assets/screenshot-pre-upload.png" alt="Upload page" width="500"></td>
    <td><img src="assets/screenshot-importing.png" alt="Import in progress" width="500"></td>
  </tr>
  <tr>
    <td><img src="assets/screenshot-post-upload.png" alt="Upload complete" width="500"></td>
    <td><img src="assets/screenshot-chat.png" alt="Chat UI with RAG sources" width="500"></td>
  </tr>
</table>

## Features

- **Import your ChatGPT conversation history** — upload your full export; supports multi-file exports (ChatGPT now splits large exports across several JSON files) and histories of thousands of conversations
- **Project support** — conversations organised into ChatGPT projects are imported into project folders in MattGPT, with collapsible folder navigation and user-assignable names
- **RAG memory** — conversations are summarised, embedded, and indexed so any LLM can retrieve relevant context from your history when you chat
- **Multi-turn chat** — full conversation support with rolling summaries that keep context coherent across long sessions, even with small-context local models
- **Persistent chat sessions** — conversations in MattGPT are saved to MongoDB and embedded in the vector store, so they become part of your searchable memory over time
- **Chat history sidebar** — browse and resume past chat sessions, and read any imported conversation in a read-only viewer directly in the app
- **Clickable source citations** — each LLM response shows which past conversations were used as context; click any source to read the original conversation
- **Configurable RAG modes** — choose between full automatic injection (`WithPrompt`), hybrid auto-RAG + tool-calling (`Auto`), or tool-only retrieval (`ToolsOnly`)
- **Multiple LLM providers** — works with Ollama (local, default), Foundry Local, Azure OpenAI, OpenAI, Anthropic Claude, and Google Gemini
- **Multiple vector stores** — supports Qdrant (default), Azure AI Search, Pinecone, and Weaviate

## Goals

Enable users to import their entire ChatGPT conversation history into a format that can be used as RAG memory for any Large Language Model. This allows users to leverage their past interactions with ChatGPT to enhance responses from other LLMs.

## Architecture

A .NET Aspire application consisting of:

- **Blazor web frontend** — upload UI and chat UI
- **ASP.NET Core API** — parsing, background processing, RAG pipeline
- **MongoDB** — stores full conversation data and metadata
- **Vector store** — stores embeddings for semantic search (Qdrant, Azure AI Search, Pinecone, or Weaviate)
- **LLM** — config-driven: Ollama, Foundry Local, Azure OpenAI, OpenAI, Anthropic, or Gemini


## Quick Start

```bash
# Prerequisites: .NET 10 SDK, Docker Desktop
git clone https://github.com/matt-goldman/MattGPT.git
cd MattGPT/src/MattGPT.Web && npm install && cd ../..

# Pull default Ollama models
ollama pull llama3.2
ollama pull nomic-embed-text

# Start everything via Aspire
cd src/MattGPT.AppHost
dotnet run
```

The Aspire dashboard URL will be printed to the console. The web UI URL is also shown on startup.


## Documentation

| Document | Description |
|----------|-------------|
| [Getting Started](docs/getting-started.md) | Prerequisites, setup, and first run |
| [Configuration](docs/configuration.md) | LLM, vector store, and RAG settings |
| [Integrations](docs/integrations.md) | Setup guides for each LLM and vector store provider |
| [Usage](docs/usage.md) | Uploading conversations, using the chat UI, API endpoints |
| [Troubleshooting](docs/troubleshooting.md) | Common issues, performance notes |


## Project Tracking

Planning and issue tracking lives in the [`docs/`](docs/) folder — [`docs/index.md`](docs/index.md) is the system of record. This file-based backlog exists so that AI coding agents (both online and offline) can pick up work autonomously. Completed issues are archived in `docs/Done/` with full context of what was built and why.

If you'd like to suggest a feature or report a bug, please [open a GitHub Issue](https://github.com/matt-goldman/MattGPT/issues). Approved items will be promoted into the docs backlog for implementation.


## Future Enhancements

- **Runtime configuration wizard** — a guided setup experience so new users can configure the LLM provider and model without editing config files (see [issue #14](docs/TODO/014-runtime-llm-configuration-wizard.md)).
- Advanced parsing: sentiment analysis, topic modelling, entity extraction.
- Import of other file types (images, PDFs) shared in conversations.
- Integration with LM Studio, OpenWebUI, and other LLM tools.
- Automatic project reconstruction in other LLMs from imported history.

