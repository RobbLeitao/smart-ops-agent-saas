Configuring OpenAI for Semantic Kernel

The application supports using OpenAI as a chat completion provider for Semantic Kernel. To enable it, set the following environment variables in your deployment or local environment:

- OPENAI_API_KEY: Your OpenAI API key. If this is not set, the application will start without registering the OpenAI provider (useful for local testing).
- OPENAI_MODEL_ID: The model identifier to use (e.g., 'gpt-4o', 'gpt-4o-mini'). Defaults to 'gpt-4o' if not provided.

Security note: Do not commit your real API keys to the repository. Use secret managers or environment variables in CI/CD.
