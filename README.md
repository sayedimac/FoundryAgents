# mslearn-ai-agents (local lab workspace)

This repo is organized to mirror the Microsoft Learning labs for **Develop AI Agents in Azure**:
https://microsoftlearning.github.io/mslearn-ai-agents/

## Labs

- **Lab 02 – Develop an AI agent** (C# / .NET)
  - Project: `Labfiles/02-build-ai-agent/dotnet/GitHubAgent`
  - Readme: `Labfiles/02-build-ai-agent/dotnet/GitHubAgent/README.md`

## Quick start (Lab 02)

From the repo root:

```powershell
cd .\Labfiles\02-build-ai-agent\dotnet\GitHubAgent

dotnet build
# interactive mode
# (if the app supports an 'interactive' arg)
dotnet run -- interactive
```

### Configuration

For local development, set your Foundry project endpoint and deployment name in:

- `Labfiles/02-build-ai-agent/dotnet/GitHubAgent/appsettings.Development.json`

Notes:

- Avoid committing secrets. Prefer local-only config or environment variables.
- If you see a root-level `appsettings.Development.json`, it is not used by the Lab 02 project unless you explicitly wire it in.
