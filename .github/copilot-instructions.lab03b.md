# Lab 03b Copilot Instructions - Multi-agent with Foundry

Project: `03-multi-agent/FrontEndAgent` (maps to Learn Lab 03b)

## Config

- `Azure:ProjectEndpoint`
- `Azure:ModelDeploymentName`
- Optional: `Azure:MsLearnAgentName` (defaults to `MSLearn`)

## Run

```powershell
cd .\03-multi-agent

dotnet build .\Lab03b.sln
cd .\FrontEndAgent

dotnet run
```

## Behavior

- Creates a front-end agent.
- Calls an existing remote agent (by name) for research.
