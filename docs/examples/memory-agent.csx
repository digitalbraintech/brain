await using IDigitalBrain digitalBrain = await DigitalBrainClient.ConnectAsync(args);
if (Signal is not UserMessaged message) return;
if (message.Text.Length < 24) return;
var memory = digitalBrain.GetEntity<IMemory>("default");
await memory.Upsert(new MemoryFact(
    Key: message.CommandId.ToString(),
    Text: message.Text,
    SourceChat: message.Chat.Name,
    RecordedAt: DateTimeOffset.UtcNow,
    CommandId: message.CommandId.ToString()));
await digitalBrain.PublishAsync(new MemoryUpdated(message.CommandId.ToString(), message.Chat.Name));
