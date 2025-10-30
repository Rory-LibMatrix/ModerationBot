using System.Text.Json.Serialization;
using LibMatrix;
using LibMatrix.RoomTypes;

namespace ModerationBot;

public class PolicyList {
    [JsonIgnore]
    public GenericRoom Room { get; set; }

    [JsonPropertyName("trusted")]
    public bool Trusted { get; set; } = false;

    [JsonIgnore]
    public List<StateEventResponse> Policies { get; set; } = new();
}
