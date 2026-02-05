# Lab 05 Copilot Instructions - Agent orchestration (Agent Framework)

Folder: `Labfiles/05-agent-orchestration/dotnet`

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
   - `dotnet build Labfiles/05-agent-orchestration/dotnet/Lab05.sln`
   - `dotnet run --project Labfiles/05-agent-orchestration/dotnet/CustomerFeedbackOrchestration/CustomerFeedbackOrchestration.csproj`
