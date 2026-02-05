# Lab 06 Copilot Instructions - Remote agents with A2A protocol

Folder: `Labfiles/06-multi-remote-agents-with-a2a/dotnet`

Goal:

- A2A-style remote agents for technical writing assistance (title agent + outline agent + routing agent).

## Status

- Implemented: `A2AHost` (web API) + `A2AClient` (console).

## Run

1. Configure Azure OpenAI for the host (env vars or `A2AHost/appsettings.Development.json`):
   - `AZURE_OPENAI_ENDPOINT`
   - `AZURE_OPENAI_DEPLOYMENT_NAME`
2. Authenticate: `az login`
3. Start the host (example):
   - `set ASPNETCORE_URLS=http://localhost:5000`
   - `dotnet run --project Labfiles/06-multi-remote-agents-with-a2a/dotnet/A2AHost/A2AHost.csproj`
4. Run the client:
   - `dotnet run --project Labfiles/06-multi-remote-agents-with-a2a/dotnet/A2AClient/A2AClient.csproj`
