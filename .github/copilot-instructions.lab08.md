# Lab 08 Copilot Instructions - Workflow in Microsoft Foundry

Project: `Labfiles/08-build-workflow-ms-foundry/dotnet/FoundryWorkflowDemo`

## Run

```powershell
cd .\Labfiles\08-build-workflow-ms-foundry\dotnet

dotnet build .\Lab08.sln
cd .\FoundryWorkflowDemo

dotnet run
```

## Behavior

- Demonstrates a workflow-like sequence (planner -> writer -> reviewer).
- Cleans up ephemeral agents.
