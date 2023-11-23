using System.Text.Json.Serialization;
using LibMatrix.EventTypes;

namespace ModerationBot.StateEventTypes.Policies.Implementations;

/// <summary>
///     Homeserver media policy event, entity is the MXC URI of the file, hashed with SHA3-256.
/// </summary>
[MatrixEvent(EventName = "gay.rory.moderation.rule.media.homeserver")]
public class MediaPolicyHomeserver : BasePolicy {
}
