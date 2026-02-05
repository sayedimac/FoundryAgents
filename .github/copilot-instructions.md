# Repo Copilot Instructions (mslearn-ai-agents / .NET 10)

This workspace mirrors the Microsoft Learning labs:
https://microsoftlearning.github.io/mslearn-ai-agents/

## Structure

- Each lab is under `Labfiles/<lab-id>/dotnet/<project>`
- All projects target `net10.0`
- Foundry/Agent Service samples use the **new Foundry SDK** (`Azure.AI.Projects` + `Azure.AI.Projects.OpenAI`) and the **Responses** API.

## Common configuration

Most apps read configuration from (in order):

1. `appsettings.Development.json` (optional)
2. Environment variables

Common keys:

- `Azure:ProjectEndpoint`
- `Azure:ModelDeploymentName`

## Build / run

- Prefer `dotnet build` at the solution level (each lab folder has its own `.sln`).
- For interactive console apps, run with `dotnet run`.

## Coding conventions

- Keep labs independent (no shared project references unless necessary).
- Prefer minimal, readable samples over large abstractions.
- If using preview APIs (Foundry SDK), keep `<EnablePreviewFeatures>true</EnablePreviewFeatures>`.
