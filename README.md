# Develop AI Agents on Azure (Azure AI Foundry) — Lab Workspace

This workspace follows the Microsoft Learn path:

- Training path: https://learn.microsoft.com/en-us/training/paths/develop-ai-agents-on-azure/
- Lab repo (instructions + assets): https://microsoftlearning.github.io/mslearn-ai-agents/

This repo is a **re-organized local workspace**: lab folders are placed directly under the repo root with shorter names.

## Folder layout

- `01-simple-agent` (maps to Learn **Lab 02** — Develop an AI agent)
- `02-custom-functions` (maps to Learn **Lab 03** — Agent custom functions)
- `03-multi-agent` (maps to Learn **Lab 03b** — Multi-agent solution)
- `04-mcp-tools` (maps to Learn **Lab 03c** — Use agent tools with MCP)
- `05-semantic-kernel` (maps to Learn **Lab 04** — Semantic Kernel)
- `06-orchestration` (maps to Learn **Lab 05** — Agent orchestration)
- `07-a2a` (maps to Learn **Lab 06** — Multi-remote agents with A2A)
- `08-foundry-workflow` (maps to Learn **Lab 08** — Build workflow with Foundry)
- `09-foundry-iq` (maps to Learn **Lab 09** — Integrate agent with Foundry IQ)

Each folder contains its own `.sln` and project(s). The root [GitHubAgent.sln](GitHubAgent.sln) includes all projects for convenience.

## Quick start

Build everything:

```powershell
dotnet build .\GitHubAgent.sln
```

Run the simple agent (Learn Lab 02):

```powershell
cd .\01-simple-agent\GitHubAgent
dotnet run -- interactive
```

Run the web app demo (ASP.NET Core):

```powershell
dotnet run --project .\AgentWebApp.csproj
```

## Configuration

Most projects use `appsettings.Development.json` (local-only) and/or environment variables.

Common keys:

```json
{
  "Azure": {
    "ProjectEndpoint": "https://<your-project>.services.ai.azure.com/api/projects/<project>",
    "ModelDeploymentName": "<your-model-deployment>"
  }
}
```

## Where is the agent hosted?

These labs can run in three different places; the README(s) in each lab folder call out which modes it supports:

- **Local (your machine)**: you run `dotnet run` and the app calls Azure AI Foundry using `DefaultAzureCredential` (for example `az login`).
- **Local container playground** (optional in some labs): you open the Foundry VS Code playground locally, then run the app in “container agent” mode.
- **Hosted in Azure AI Foundry**: when you deploy, the agent runs as a **Hosted Agent** in your Azure AI Foundry project’s **capability host** (container runtime managed by the service). This is the “hosted” part.

Notes:

- Don’t commit secrets. Keep `appsettings.Development.json` and `.env` local.
- For hosted runs, identity typically comes from a **managed identity** associated with the Foundry project/capability host; you’ll need the appropriate role assignments (for example the built-in **Azure AI User** role).

## Copilot instructions

Repo-wide guidance for Copilot lives in [.github/copilot-instructions.md](.github/copilot-instructions.md), with per-lab variants in `.github/copilot-instructions.lab*.md`.
