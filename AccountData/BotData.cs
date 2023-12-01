using System.Text.Json.Serialization;

namespace ModerationBot.AccountData;

public class BotData {
    [JsonPropertyName("control_room")]
    public string ControlRoom { get; set; } = "";

    [JsonPropertyName("log_room")]
    public string? LogRoom { get; set; } = "";

    [JsonPropertyName("default_policy_room")]
    public string? DefaultPolicyRoom { get; set; }
}
