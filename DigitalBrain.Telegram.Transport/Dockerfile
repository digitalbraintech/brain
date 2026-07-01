# Container image for the DigitalBrain Telegram transport.
# Structurally mirrors DigitalBrain.Kernel/Dockerfile — only the project path and exposed port differ.
#
# Usage (example):
#   docker build -t digitalbrain-telegram:latest -f DigitalBrain.Telegram.Transport/Dockerfile .
#   Then push and reference from deploy/Program.cs (Azure Container App).

ARG DOTNET_VERSION=11.0-preview

# Build
FROM mcr.microsoft.com/dotnet/sdk:11.0-preview AS build
WORKDIR /src

COPY . .
RUN dotnet restore "DigitalBrain.Telegram.Transport/DigitalBrain.Telegram.Transport.csproj"
RUN dotnet publish "DigitalBrain.Telegram.Transport/DigitalBrain.Telegram.Transport.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime (small)
FROM mcr.microsoft.com/dotnet/aspnet:11.0-preview AS final
WORKDIR /app
COPY --from=build /app/publish .

# Telegram webhook endpoint — Telegram POSTs to /webhook on this port.
EXPOSE 8080

ENTRYPOINT ["dotnet", "DigitalBrain.Telegram.Transport.dll"]
