# Basic container image for the DigitalBrain silo host.
# This is the deployable "marketplace silo".
#
# Usage (example):
#   docker build -t digitalbrain-silo:latest -f DigitalBrain.Silo/Dockerfile .
#   Then push and reference from your DeploymentKit Pulumi components (Azure Container App).

ARG DOTNET_VERSION=11.0-preview

# Build
FROM mcr.microsoft.com/dotnet/sdk:11.0-preview AS build
WORKDIR /src

COPY . .
RUN dotnet restore "DigitalBrain.Silo/DigitalBrain.Silo.csproj"
RUN dotnet publish "DigitalBrain.Silo/DigitalBrain.Silo.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime (small)
FROM mcr.microsoft.com/dotnet/aspnet:11.0-preview AS final
WORKDIR /app
COPY --from=build /app/publish .

# Orleans silo typically listens on specific ports; expose common ones
EXPOSE 11111 30000 8080

ENTRYPOINT ["dotnet", "DigitalBrain.Silo.dll"]