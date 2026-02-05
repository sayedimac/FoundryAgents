# Lab 09 Copilot Instructions - Foundry IQ integration

Project: `Labfiles/09-integrate-agent-with-foundry-iq/dotnet/FoundryIQClient`

## Assumption

- You created an agent in Foundry Portal and enabled Foundry IQ knowledge access.

## Config

- `Azure:ProjectEndpoint`
- Optional: `Azure:FoundryIqAgentName` (defaults to `FoundryIQAgent`)

## Run

```powershell
cd .\Labfiles\09-integrate-agent-with-foundry-iq\dotnet

dotnet build .\Lab09.sln
cd .\FoundryIQClient

dotnet run
```

## Behavior

- Connects to the portal-created agent by name.
- Prompts you to approve/deny MCP tool usage.
