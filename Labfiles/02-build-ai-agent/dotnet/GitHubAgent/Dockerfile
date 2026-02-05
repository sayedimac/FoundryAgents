# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files for dependency resolution
COPY *.csproj* .

# Restore packages
RUN dotnet restore

# Copy everything else and build
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore -p:AssemblyName=app

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8088

# Set default ASP.NET Core environment variables
ENV ASPNETCORE_URLS=http://+:8088
ENV ASPNETCORE_ENVIRONMENT=Production

# Note: appsettings.json is included in the publish output
# Environment-specific configuration should be provided via environment variables at deployment time

# Use ENTRYPOINT with the main DLL
ENTRYPOINT ["dotnet", "GitHubAgent.dll"]
