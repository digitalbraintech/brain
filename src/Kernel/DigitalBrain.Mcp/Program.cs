using DigitalBrain.Aspire;
using DigitalBrain.Mcp;
using DigitalBrain.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddDigitalBrainClient();
builder.Services
    .AddMcpServer()
    // URL-mode elicitation is negotiated at initialization. Preserve the peer's
    // capabilities in its MCP session; stateless SDK servers expose none here.
    .WithHttpTransport(options => options.Stateless = false)
    .WithTools<ChatTools>()
    .WithTools<GraphTools>();

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcp(McpSurface.Path);
app.Run();
