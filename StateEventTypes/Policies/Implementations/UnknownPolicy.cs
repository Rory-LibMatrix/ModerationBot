using System.Text.Json.Serialization;
using LibMatrix.EventTypes;

namespace ModerationBot.StateEventTypes.Policies.Implementations;

/// <summary>
///     Unknown policy event, usually used for handling unknown cases
/// </summary>
public class UnknownPolicy : BasePolicy {
}
