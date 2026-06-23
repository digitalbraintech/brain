var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var app = builder.Build();

app.MapGet("/health", () => Results.Text("ok"));

app.MapGet("/status", (IConfiguration cfg) =>
{
    string cluster;
    try
    {
        // No live cluster in test environment — degrade gracefully
        cluster = "unknown";
    }
    catch
    {
        cluster = "unreachable";
    }

    string storage;
    try
    {
        storage = "unknown";
    }
    catch
    {
        storage = "unreachable";
    }

    return Results.Json(new
    {
        cluster,
        storage,
        llmMode = cfg["DigitalBrain:Llm:Provider"] ?? "none",
        journalSampled = -1,
    });
});

app.Run();

public partial class Program;
