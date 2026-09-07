namespace DigitalBrain.Kernel;

internal static class HttpSurfacePaths
{
    public const string OwnerCommandsPath = "/owner/commands";
    public const string ChatEventsPath = "/chats/{chatName}/events";
    public const string SurfaceEventsPath = "/surfaces/{surfaceName}/events";

    public const string EventStreamContentType = "text/event-stream";
    public const string CacheControlNoCache = "no-cache";
    public const string SurfaceOpenedEvent = "surface-opened";
    public const string ChatTurnEvent = "chat-turn";
    public const string ChatDeltaEvent = "chat-delta";
    public const string ChatErrorEvent = "chat-error";
    public const string ChatAcceptedEvent = "chat-accepted";

    public const string KindChatSend = "chat.send";
    public const string KindChatCancelTurn = "chat.cancel-turn";
    public const string KindSurfaceOpen = "surface.open";

    // Multipart voice note → STT → same durable chat.send path.
    public const string ChatVoicePath = "/chats/{chatName}/voice";

    // Read-only kit entity state, resolved under the HTTP actor's own principal partition.
    public const string KitChartPath = "/kit/charts/{chartName}";
    public const string KitImagePath = "/kit/images/{imageName}";
    public const string KitImageContentPath = "/kit/images/{imageName}/content";
    public const string KitGraphPath = "/kit/graphs/{graphName}";
    public const string KitSurfacePath = "/kit/surfaces/{surfaceName}";

    public const string BrainGraphPath = "/chats/{chatName}/brain";
    public const string BrainGraphSubscriptionsPath = "/chats/{chatName}/brain/subscriptions";
    public const string BrainActivitiesPath = "/chats/{chatName}/activities";
}
