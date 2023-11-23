using System.Text.Json.Serialization;
using LibMatrix.EventTypes;

namespace ModerationBot.StateEventTypes.Policies.Implementations;

/// <summary>
///     Text contains policy event, entity is the text to contain.
/// </summary>
[MatrixEvent(EventName = "gay.rory.moderation.rule.text.contains")]
public class MessagePolicyContainsText : BasePolicy {
}
