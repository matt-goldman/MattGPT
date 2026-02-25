# MattGPT

A solution for importing an entire ChatGPT conversation history for use as RAG memory in any LLM

## Goals

Enable users to import their entire ChatGPT conversation history, including all conversations and messages, into a format that can be used as Retrieval-Augmented Generation (RAG) memory for any Large Language Model (LLM). This allows users to leverage their past interactions with ChatGPT to enhance the performance of other LLMs.

## Solution Structure

This will be an Aspire project that can be run entirely locally and offline for testing and demo purposes, or deployed and configurable to connect to different endpoints for LLMs and vector databases.

The offline version will be completed first, and will include (all running locally in Aspire):

 - A UI (can be Razor Pages or Blazor) that allows users to upload their ChatGPT conversation history (in JSON format). Given this may be a long-running operation, processing should happen on a background thread but have observability in the UI to show progress and completion status.
 - A parser that processes the uploaded JSON and extracts conversations and messages
 - Qdrant as the vector database to store the extracted conversations and messages as RAG memory
 - A simple interface to query the stored conversations and messages, demonstrating how they can be used as RAG memory for an LLM
 - MongoDB as the primary database to store metadata about the conversations and messages, such as timestamps, conversation titles, etc.
 - A UI to test interacting with an LLM using the stored conversations and messages as RAG memory, demonstrating the enhanced performance of the LLM when utilizing this memory (can be two pages in the UI - one for uploading and processing the ChatGPT conversation history, and another for testing the LLM interaction with the stored RAG memory)
 - Config driven selection of Foundry Local, Ollama, or Azure OpenAI as the LLM endpoint for testing the RAG memory

## Future Enhancements

    - Support for additional vector databases (e.g., Pinecone, Weaviate) and LLM endpoints (e.g., Hugging Face, Cohere)
    - Advanced parsing and processing of the ChatGPT conversation history to extract more nuanced information, such as sentiment analysis, topic modeling, etc.
    - Integration with other tools and platforms to allow users to easily import their ChatGPT conversation history from various sources (e.g., Google Drive, Dropbox, etc.)
    - Enhanced UI/UX for a more seamless and intuitive user experience when uploading, processing, and utilizing the ChatGPT conversation history as RAG memory.
    - Import of other files (like generated images, PDFs, etc.) that may have been shared in the ChatGPT conversations, and storing them in a way that they can also be used as RAG memory for LLMs.
    - Integration into existing LLM tooling, like LMStudio, OpenWebUI, etc., to allow users to easily utilize their ChatGPT conversation history as RAG memory within their existing workflows and tools.
    - Automatic reconstruction of projects and conversations in other LLMs based on the imported ChatGPT conversation history, allowing users to seamlessly transition their work and interactions from ChatGPT to other LLMs without losing context or information.

## Documentation Requirements

Required documentation should be minimal as the system should be fairly straightforward to use, but should include:

 - Instructions for running the Aspire project locally, including any necessary setup steps (e.g., installing dependencies, configuring databases, etc.)
 - Instructions for uploading and processing the ChatGPT conversation history, including the expected format of the JSON file and any limitations or requirements
 - Instructions for testing the LLM interaction with the stored RAG memory, including how to select different LLM endpoints and interpret the results
 - Any additional documentation or resources that may be helpful for users to understand and utilize the system effectively.
