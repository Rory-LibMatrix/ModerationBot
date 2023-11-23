using System.Text.Json.Serialization;
using LibMatrix.EventTypes;

namespace ModerationBot.StateEventTypes.Policies.Implementations;

/// <summary>
///     File policy event, entity is the MXC URI of the file, hashed with SHA3-256.
/// </summary>
[MatrixEvent(EventName = "gay.rory.moderation.rule.media")]
public class MediaPolicyFile : BasePolicy {
    /// <summary>
    ///     Hash of the file
    /// </summary>
    [JsonPropertyName("file_hash")]
    public string? FileHash { get; set; }
}
