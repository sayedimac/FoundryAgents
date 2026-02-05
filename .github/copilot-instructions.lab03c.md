# Lab 03c Copilot Instructions - Remote MCP (Microsoft Learn MCP)

Project: `Labfiles/03c-use-agent-tools-with-mcp/dotnet/MsLearnMcpAgent`

## Config

- `Azure:ProjectEndpoint`
- `Azure:ModelDeploymentName`
- Optional: `Azure:MsLearnMcpServerUrl` (defaults to `https://learn.microsoft.com/api/mcp`)

## Run

```powershell
cd .\Labfiles\03c-use-agent-tools-with-mcp\dotnet

dotnet build .\Lab03c.sln
cd .\MsLearnMcpAgent

dotnet run
```

## Behavior

- Creates an ephemeral agent with an MCP tool.
- Auto-approves MCP tool calls.
