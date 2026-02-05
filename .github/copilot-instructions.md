# Repo Copilot Instructions (Azure AI Foundry Agents labs / .NET)

This workspace follows the Microsoft Learn path:
https://learn.microsoft.com/en-us/training/paths/develop-ai-agents-on-azure/

Labs and assets:
https://microsoftlearning.github.io/mslearn-ai-agents/

## Structure

- Lab folders live directly under the repo root: `01-*`, `02-*`, …
- All .NET projects target `net10.0` and enable preview features.
- Azure AI Foundry samples use the **Azure AI Foundry Projects SDK** (`Azure.AI.Projects` + `Azure.AI.Projects.OpenAI`) and the **Responses** API.

## Where the agent runs (hosting)

Be explicit about the runtime when writing instructions or code:

- **Local run**: `dotnet run` executes on the developer machine and calls Azure AI Foundry over HTTPS.
- **Hosted Agent**: after deployment, the agent runs in the Foundry project’s **capability host** (remote container runtime). Authentication is typically via managed identity.

## Common configuration

Most apps read configuration from:

1. `appsettings.Development.json` (local-only)
2. Environment variables

Common keys:

- `Azure:ProjectEndpoint`
- `Azure:ModelDeploymentName`

## Build / run

- Prefer `dotnet build` on the root [GitHubAgent.sln](../GitHubAgent.sln) or the lab’s local `.sln`.
- For interactive console apps, prefer `dotnet run -- interactive` when supported.

## Coding conventions

- Keep labs independent (no shared project references unless necessary).
- Prefer minimal, readable samples over heavy abstractions.
- Preserve preview settings used by the labs (`<EnablePreviewFeatures>true</EnablePreviewFeatures>`).
