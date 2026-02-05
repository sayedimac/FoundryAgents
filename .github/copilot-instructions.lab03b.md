# Lab 03b Copilot Instructions - Multi-agent with Foundry

Project: `Labfiles/03b-build-multi-agent-solution/dotnet/FrontEndAgent`

## Config

- `Azure:ProjectEndpoint`
- `Azure:ModelDeploymentName`
- Optional: `Azure:MsLearnAgentName` (defaults to `MSLearn`)

## Run

```powershell
cd .\Labfiles\03b-build-multi-agent-solution\dotnet

dotnet build .\Lab03b.sln
cd .\FrontEndAgent

dotnet run
```

## Behavior

- Creates a front-end agent.
- Calls an existing remote agent (by name) for research.
