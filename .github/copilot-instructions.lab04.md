# Lab 04 Copilot Instructions - Microsoft Agent Framework SDK

Folder: `Labfiles/04-semantic-kernel/dotnet`

Goal:

- Build an expense-claim processing agent using the Microsoft Agent Framework SDK.

## Status

- Implemented: `ExpenseClaimAgent` console app.

## Run

1. Set config (either env vars or `appsettings.Development.json`):
   - `AZURE_OPENAI_ENDPOINT` (or `Azure:OpenAIEndpoint`)
   - `AZURE_OPENAI_DEPLOYMENT_NAME` (or `Azure:OpenAIDeploymentName`)
   - Optional: `AZURE_OPENAI_KEY` (or `Azure:OpenAIKey`). If omitted, uses `AzureCliCredential`.
2. Authenticate (if not using an API key): `az login`
3. Build/run:
   - `dotnet build Labfiles/04-semantic-kernel/dotnet/Lab04.sln`
   - `dotnet run --project Labfiles/04-semantic-kernel/dotnet/ExpenseClaimAgent/ExpenseClaimAgent.csproj`
