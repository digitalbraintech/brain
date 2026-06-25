{
  "name": "local-dev-brain",
  "description": "Simple local DigitalBrain spin with reusable integrations from marketplace: Telegram bot (no core logic) and Flutter client (Aspire integration to start Windows). Run: dotnet run --project NeuroOSPrototype.AppHost brain.example.sc",
  "packs": [
    {
      "name": "Telegram.Bot",
      "version": "1.0",
      "description": "Packed Telegram bot integration. Installable experience. Configure token via env or synapse. No logic inside core brain.",
      "config": {
        "tokenEnv": "TELEGRAM_BOT_TOKEN"
      }
    },
    {
      "name": "DigitalBrain.UI.AspireFlutter",
      "version": "0.1.0",
      "description": "Flutter as marketplace pack. Comes with Aspire integration to start Flutter Windows (or web-server). Wires to brain gateway for surfaces and RfwCards.",
      "config": {
        "target": "windows"
      }
    }
  ],
  "aspire": {
    "start": "default-local",
    "useNeuron": true
  }
}