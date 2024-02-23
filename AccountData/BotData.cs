using System.Text.Json.Serialization;
using LibMatrix.EventTypes;

namespace ModerationBot.AccountData;

[MatrixEvent(EventName = EventId)]
public class BotData {
    public const string EventId = "gay.rory.moderation_bot_data";

    [JsonPropertyName("control_room")]
    public string? ControlRoom { get; set; } = "";

    [JsonPropertyName("log_room")]
    public string? LogRoom { get; set; } = "";

    [JsonPropertyName("default_policy_room")]
    public string? DefaultPolicyRoom { get; set; }
}