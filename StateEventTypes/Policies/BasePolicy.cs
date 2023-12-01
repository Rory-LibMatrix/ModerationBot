using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using LibMatrix;
using LibMatrix.Interfaces;

namespace ModerationBot.StateEventTypes.Policies;

public abstract class BasePolicy : EventContent {
    /// <summary>
    ///     Entity this policy applies to, null if event was redacted
    /// </summary>
    [JsonPropertyName("entity")]
    public string? Entity { get; set; }

    /// <summary>
    ///     Reason this policy exists
    /// </summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>
    ///     Suggested action to take, one of `ban`, `kick`, `mute`, `redact`, `spoiler`, `warn` or `warn_admins`
    /// </summary>
    [JsonPropertyName("recommendation")]
    [AllowedValues("ban", "kick", "mute", "redact", "spoiler", "warn", "warn_admins")]
    public string Recommendation { get; set; } = "warn";

    /// <summary>
    ///     Expiry time in milliseconds since the unix epoch, or null if the ban has no expiry.
    /// </summary>
    [JsonPropertyName("support.feline.policy.expiry.rev.2")] //stable prefix: expiry, msc pending
    public long? Expiry { get; set; }

    //utils
    /// <summary>
    ///     Readable expiry time, provided for easy interaction
    /// </summary>
    [JsonPropertyName("gay.rory.matrix_room_utils.readable_expiry_time_utc")]
    public DateTime? ExpiryDateTime {
        get => Expiry == null ? null : DateTimeOffset.FromUnixTimeMilliseconds(Expiry.Value).DateTime;
        set => Expiry = value is null ? null : ((DateTimeOffset)value).ToUnixTimeMilliseconds();
    }

    #region Internal metadata

    [JsonIgnore]
    public PolicyList PolicyList { get; set; }

    public StateEventResponse OriginalEvent { get; set; }

    #endregion
}
