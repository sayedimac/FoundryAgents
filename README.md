# Writer-Reviewer Hosted Agent Sample

This sample demonstrates the construction of a hosted agent workflow using the Agent Framework. It features two agents—a "Writer" and a "Reviewer"—integrated in a streamlined, one-way workflow to illustrate best practices for agent orchestration and interaction.

## Project Structure

| File                           | Role                  | Description                                                                                                |
| ------------------------------ | --------------------- | ---------------------------------------------------------------------------------------------------------- |
| `WorkflowCore.cs`              | Workflow Logic        | Implements the end-to-end workflow, orchestrating interactions between Writer and Reviewer agents          |
| `Program.cs`                   | Application Entry     | Configures and launches the hosted agent, manages command-line arguments, and initiates the workflow       |
| `appsettings.Development.json` | Environment Settings  | Local development settings. Contains Microsoft Foundry Project endpoint and model deployment configuration |
| `GitHubAgent.csproj`         | Project Configuration | Specifies NuGet dependencies and build parameters                                                          |

## Prerequisites

- .NET 9 SDK or higher.
- A Microsoft Foundry Project and a model deployment.

## Setup and Installation

1. Download and install the .NET 9 SDK from the [official .NET website](https://dotnet.microsoft.com/download).

2. Restore NuGet packages.

   ```bash
   dotnet restore
   ```

3. Local development configuration

   - Create or update `appsettings.Development.json` with your Microsoft Foundry Project configuration for local development only:

   ```json
   {
     "Azure": {
       "ProjectEndpoint": "https://YOUR-RESOURCE.services.ai.azure.com/api/projects/YOUR-PROJECT",
       "ModelDeploymentName": "YOUR-MODEL-DEPLOYMENT"
     }
   }
   ```

   **⚠️ IMPORTANT**: Never commit secrets to version control. Add `appsettings.Development.json` and `.env` to `.gitignore` if you store secrets locally.

## Local Testing

This sample authenticates using [DefaultAzureCredential](https://learn.microsoft.com/dotnet/azure/sdk/authentication/credential-chains?tabs=dac#defaultazurecredential-overview). Ensure your development environment is configured to provide credentials via one of the supported sources, for example:

- Azure CLI (`az login`)
- Visual Studio Code account sign-in
- Visual Studio account sign-in

Confirm authentication locally (for example, az account show or az account get-access-token) before running the sample.

### Interactive Mode

Run the hosted agent directly for development and testing:

```bash
dotnet build
dotnet run interactive
```

### Container Mode

To run the agent in container mode:

1. Open the Visual Studio Code Command Palette and execute the `Microsoft Foundry: Open Container Agent Playground Locally` command.
2. Use the following command to initialize the containerized hosted agent.
   ```bash
   dotnet build
   dotnet run
   ```
3. Submit a request to the agent through the playground interface. For example, you may enter a prompt such as: "Create a slogan for a new electric SUV that is affordable and fun to drive."
4. Review the agent's response in the playground interface.

> **Note**: Open the local playground before starting the container agent to ensure the visualization functions correctly.

## Deployment
 
**Preparation (required)**

- Before running the `Deploy Hosted Agent` command, create or update a `.env` file at the workspace root with the production/container values your application expects (for example the Foundry project endpoint and model deployment name). The extension reads this `.env` and forwards it to the hosted agent runtime during creation. Example `.env` entries:

```env
AZURE_AI_PROJECT_ENDPOINT="https://YOUR-RESOURCE.services.ai.azure.com/api/projects/YOUR-PROJECT"
AZURE_AI_MODEL_DEPLOYMENT_NAME="YOUR-MODEL-DEPLOYMENT"
```

- Do not commit `.env` to source control if it contains secrets; prefer secure stores like Key Vault for secrets.

To deploy the hosted agent:

1. Open the Visual Studio Code Command Palette and run the `Microsoft Foundry: Deploy Hosted Agent` command.

2. Follow the interactive deployment prompts. The extension will help you select or create the container files it needs:
   - It first looks for a `Dockerfile` at the repository root. If not found, you can select an existing `Dockerfile` or generate a new one.
   - If you choose to generate a Dockerfile, the extension will place the files at the repo root and open the `Dockerfile` in the editor; the deployment flow is intentionally cancelled in that case so you can review and edit the generated files before re-running the deploy command.

3. What the deploy flow does for you:
   - Creates or obtains an Azure Container Registry for the target project.
   - Builds and pushes a container image from your workspace (the build packages the workspace respecting `.dockerignore`).
   - Creates an agent version in Microsoft Foundry using the built image. If a `.env` file exists at the workspace root, the extension will parse it and include its key/value pairs as the hosted agent's `environment_variables` in the create request (these variables will be available to the agent runtime).
   - Starts the agent container on the project's capability host. If the capability host is not provisioned, the extension will prompt you to enable it and will guide you through creating it.

4. After deployment completes, the hosted agent appears under the `Hosted Agents (Preview)` section of the extension tree. You can select the agent there to view details and test it using the integrated playground.

**Important:**
- The extension only reads a `.env` file located at the first workspace folder root and forwards its content to the remote hosted agent runtime.

## MSI Configuration in the Azure Portal

This sample requires the Microsoft Foundry Project to authenticate using a Managed Identity when running remotely in Azure. Grant the project's managed identity the required permissions by assigning the built-in [Azure AI User](https://aka.ms/foundry-ext-project-role) role.

To configure the Managed Identity:

1. In the Azure Portal, open the Foundry Project.
2. Select "Access control (IAM)" from the left-hand menu.
3. Click "Add" and choose "Add role assignment".
4. In the role selection, search for and select "Azure AI User", then click "Next".
5. For "Assign access to", choose "Managed identity".
6. Click "Select members", locate the managed identity associated with your Foundry Project (you can search by the project name), then click "Select".
7. Click "Review + assign" to complete the assignment.
8. Allow a few minutes for the role assignment to propagate before running the application.

## Additional Resources

- [Microsoft Agents Framework](https://learn.microsoft.com/en-us/agent-framework/overview/agent-framework-overview)
- [Managed Identities for Azure Resources](https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/)
