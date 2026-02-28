# Getting Started

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for MongoDB and Qdrant containers)
- An LLM provider (one of):
  - [Ollama](https://ollama.com/) running locally (default)
  - [Foundry Local](https://learn.microsoft.com/windows/ai/foundry-local/) running locally
  - [Azure OpenAI](https://learn.microsoft.com/azure/ai-services/openai/) (cloud, requires subscription)
  - [OpenAI](https://platform.openai.com/) (cloud, requires API key)
  - [Anthropic Claude](https://docs.anthropic.com/) (cloud, requires API key)
  - [Google Gemini](https://ai.google.dev/) (cloud, requires API key)
- [Node.js](https://nodejs.org/) (for Tailwind CSS build during development)

## Setup

1. **Clone the repository**

   ```bash
   git clone https://github.com/matt-goldman/MattGPT.git
   cd MattGPT
   ```

2. **Install npm dependencies** (needed for CSS build)

   ```bash
   cd src/MattGPT.Web
   npm install
   cd ../..
   ```

3. **Configure your LLM provider**

   Edit `src/MattGPT.ApiService/appsettings.json` — see [Configuration](configuration.md) for all options, or [Integrations](integrations.md) for provider-specific examples.

   The default configuration uses Ollama with `llama3.2`. To use Ollama out of the box, ensure the required models are pulled:

   ```bash
   ollama pull llama3.2
   ollama pull nomic-embed-text
   ```

4. **Start the application**

   ```bash
   cd src/MattGPT.AppHost
   dotnet run
   ```

   Aspire will start MongoDB, Qdrant, the API service, and the web frontend automatically. The Aspire dashboard URL will be printed to the console — open it to monitor all services.

5. **Open the web UI**

   The web frontend URL is also printed on startup (e.g. `https://localhost:7xxx`). Open it in your browser.

## Next Steps

- [Upload your ChatGPT conversations](usage.md)
- [Configure providers and RAG settings](configuration.md)
- [Set up cloud LLM or vector store integrations](integrations.md)

---

← [User Guides](index.md)

→ Next: [Usage](usage.md)
