# Lab 03c Copilot Instructions - Remote MCP (Microsoft Learn MCP)

Project: `04-mcp-tools/MsLearnMcpAgent` (maps to Learn Lab 03c)

## Config

- `Azure:ProjectEndpoint`
- `Azure:ModelDeploymentName`
- Optional: `Azure:MsLearnMcpServerUrl` (defaults to `https://learn.microsoft.com/api/mcp`)

## Run

```powershell
cd .\04-mcp-tools

dotnet build .\Lab03c.sln
cd .\MsLearnMcpAgent

dotnet run
```

## Behavior

- Creates an ephemeral agent with an MCP tool.
- Auto-approves MCP tool calls.
