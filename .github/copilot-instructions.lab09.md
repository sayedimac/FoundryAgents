# Lab 09 Copilot Instructions - Foundry IQ integration

Project: `09-foundry-iq/FoundryIQClient` (maps to Learn Lab 09)

## Assumption

- You created an agent in Foundry Portal and enabled Foundry IQ knowledge access.

## Config

- `Azure:ProjectEndpoint`
- Optional: `Azure:FoundryIqAgentName` (defaults to `FoundryIQAgent`)

## Run

```powershell
cd .\09-foundry-iq

dotnet build .\Lab09.sln
cd .\FoundryIQClient

dotnet run
```

## Behavior

- Connects to the portal-created agent by name.
- Prompts you to approve/deny MCP tool usage.
