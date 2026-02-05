# Lab 05 Copilot Instructions - Agent orchestration

Folder: `06-orchestration` (maps to Learn Lab 05)

Goal:

- Sequential orchestration sample (multiple agents processing customer feedback).

## Status

- Implemented: `CustomerFeedbackOrchestration` sequential workflow.

## Run

1. Set config (either env vars or `appsettings.Development.json`):
   - `AZURE_OPENAI_ENDPOINT` (or `Azure:OpenAIEndpoint`)
   - `AZURE_OPENAI_DEPLOYMENT_NAME` (or `Azure:OpenAIDeploymentName`)
   - Optional: `AZURE_OPENAI_KEY` (or `Azure:OpenAIKey`). If omitted, uses `AzureCliCredential`.
2. Authenticate (if not using an API key): `az login`
3. Build/run:
   - `dotnet build 06-orchestration/Lab05.sln`
   - `dotnet run --project 06-orchestration/CustomerFeedbackOrchestration/CustomerFeedbackOrchestration.csproj`
