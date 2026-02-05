# Lab 04 Copilot Instructions - Semantic Kernel

Folder: `05-semantic-kernel` (maps to Learn Lab 04)

Goal:

- Build an expense-claim processing agent using Semantic Kernel.

## Status

- Implemented: `ExpenseClaimAgent` console app.

## Run

1. Set config (either env vars or `appsettings.Development.json`):
   - `AZURE_OPENAI_ENDPOINT` (or `Azure:OpenAIEndpoint`)
   - `AZURE_OPENAI_DEPLOYMENT_NAME` (or `Azure:OpenAIDeploymentName`)
   - Optional: `AZURE_OPENAI_KEY` (or `Azure:OpenAIKey`). If omitted, uses `AzureCliCredential`.
2. Authenticate (if not using an API key): `az login`
3. Build/run:
   - `dotnet build 05-semantic-kernel/Lab04.sln`
   - `dotnet run --project 05-semantic-kernel/ExpenseClaimAgent/ExpenseClaimAgent.csproj`
