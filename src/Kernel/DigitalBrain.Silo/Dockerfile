# syntax=docker/dockerfile:1
#
# Local/smoke multi-process image (silo + MCP supervised together).
# Production Hub pushes use SDK PublishContainer from .github/workflows/deploy.yml
# (same pattern as v0.1.18): separate digitalbrain-kernel and digitalbrain-mcp images.
#
# Build (repo root):
#   docker build -f src/Kernel/DigitalBrain.Silo/Dockerfile -t vhorbachov/digitalbrain-kernel:local .
#
# No secrets are baked into this image. Inject at run/deploy (Azure Key Vault → env):
#
#   Models (cloud prod — prefer API keys; Ollama is local/dev)
#     DigitalBrain__AI__OpenAI__ApiKey
#     DigitalBrain__AI__Anthropic__ApiKey
#     DigitalBrain__AI__Google__ApiKey
#     DigitalBrain__AI__XAI__ApiKey
#     DigitalBrain__AI__Default__Model
#
#   Azure Storage (Orleans clustering, reminders, grain state, journal)
#     ConnectionStrings__clustering
#     ConnectionStrings__reminders
#     ConnectionStrings__grainstate
#     ConnectionStrings__journal
#
#   Qdrant vector memory (platform dependency — not this image)
#     DigitalBrain__Memory__Provider=Qdrant
#     DigitalBrain__Memory__Qdrant__ConnectionName=memory-qdrant
#     ConnectionStrings__memory-qdrant             # Endpoint=http://qdrant:6334;Key=...
#
#   Module manifest defaults are set below; override DigitalBrain__Modules__N if needed.
#
# Ports: 8080 silo HTTP (broker + health), 5000 northbound MCP (/mcp)

FROM mcr.microsoft.com/dotnet/sdk:11.0-preview AS build
WORKDIR /src

COPY nuget.config Directory.Build.props Directory.Packages.props DigitalBrain.slnx global.json ./
COPY src/ ./src/

RUN dotnet publish src/Kernel/DigitalBrain.Silo/DigitalBrain.Silo.csproj \
      -c Release \
      -r linux-x64 \
      --self-contained false \
      -o /out/silo \
      --nologo \
 && dotnet publish src/Kernel/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj \
      -c Release \
      -r linux-x64 \
      --self-contained false \
      -o /out/mcp \
      --nologo

FROM mcr.microsoft.com/dotnet/aspnet:11.0-preview AS final
WORKDIR /app

COPY --from=build /out/silo /app/silo
COPY --from=build /out/mcp /app/mcp
COPY --chmod=755 src/Kernel/DigitalBrain.Silo/docker-entrypoint.sh /app/docker-entrypoint.sh

# Production module set matching AppHost AddModule declarations.
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0 \
    DigitalBrain__Modules__0="DigitalBrain.AI.AIModule, DigitalBrain.Modules.AI" \
    DigitalBrain__Modules__1="DigitalBrain.Memory.MemoryModule, DigitalBrain.Modules.Memory" \
    DigitalBrain__Modules__2="DigitalBrain.Time.TimeModule, DigitalBrain.Modules.Time" \
    DigitalBrain__Modules__3="DigitalBrain.Excel.ExcelModule, DigitalBrain.Modules.Excel" \
    DigitalBrain__Modules__4="DigitalBrain.Execution.ExecutionModule, DigitalBrain.Modules.Execution" \
    DigitalBrain__Modules__5="DigitalBrain.Google.GoogleModule, DigitalBrain.Modules.Google" \
    DigitalBrain__Modules__6="DigitalBrain.Salesforce.SalesforceModule, DigitalBrain.Modules.Salesforce" \
    DigitalBrain__Modules__7="DigitalBrain.UI.UIModule, DigitalBrain.Modules.UI"

EXPOSE 8080 5000

USER $APP_UID
ENTRYPOINT ["/app/docker-entrypoint.sh"]
