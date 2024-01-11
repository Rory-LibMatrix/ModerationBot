using ArcaneLibs.Extensions;
using LibMatrix;
using LibMatrix.EventTypes;
using LibMatrix.EventTypes.Spec;
using LibMatrix.EventTypes.Spec.State;
using LibMatrix.EventTypes.Spec.State.Policy;
using LibMatrix.Helpers;
using LibMatrix.Homeservers;
using LibMatrix.RoomTypes;
using LibMatrix.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModerationBot.AccountData;
using ModerationBot.StateEventTypes.Policies;

namespace ModerationBot;

public class ModerationBot(AuthenticatedHomeserverGeneric hs, ILogger<ModerationBot> logger, ModerationBotConfiguration configuration, PolicyEngine engine) : IHostedService {
    private Task _listenerTask;

    // private GenericRoom _policyRoom;
    private GenericRoom? _logRoom;
    private GenericRoom? _controlRoom;

    /// <summary>Triggered when the application host is ready to start the service.</summary>
    /// <param name="cancellationToken">Indicates that the start process has been aborted.</param>
    public async Task StartAsync(CancellationToken cancellationToken) {
        _listenerTask = Run(cancellationToken);
        logger.LogInformation("Bot started!");
    }

    private async Task Run(CancellationToken cancellationToken) {
        return;
        if (Directory.Exists("bot_data/cache"))
            Directory.GetFiles("bot_data/cache").ToList().ForEach(File.Delete);

        BotData botData;

        try {
            logger.LogInformation("Fetching bot account data...");
            botData = await hs.GetAccountDataAsync<BotData>("gay.rory.moderation_bot_data");
            logger.LogInformation("Got bot account data...");
        }
        catch (Exception e) {
            logger.LogInformation("Could not fetch bot account data... {}", e.Message);
            if (e is not MatrixException { ErrorCode: "M_NOT_FOUND" }) {
                logger.LogError("{}", e.ToString());
                throw;
            }

            botData = null;
        }

        botData = await FirstRunTasks.ConstructBotData(hs, configuration, botData);

        // _policyRoom = hs.GetRoom(botData.PolicyRoom ?? botData.ControlRoom);
        _logRoom = hs.GetRoom(botData.LogRoom ?? botData.ControlRoom);
        _controlRoom = hs.GetRoom(botData.ControlRoom);
        foreach (var configurationAdmin in configuration.Admins) {
            var pls = await _controlRoom.GetPowerLevelsAsync();
            if (pls is null) {
                await _logRoom?.SendMessageEventAsync(MessageFormatter.FormatWarning($"Control room has no m.room.power_levels?"));
                continue;
            }
            pls.SetUserPowerLevel(configurationAdmin, pls.GetUserPowerLevel(hs.UserId));
            await _controlRoom.SendStateEventAsync(RoomPowerLevelEventContent.EventId, pls);
        }
        var syncHelper = new SyncHelper(hs);

        List<string> admins = new();

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        Task.Run(async () => {
            while (!cancellationToken.IsCancellationRequested) {
                var controlRoomMembers = _controlRoom.GetMembersEnumerableAsync();
                var pls = await _controlRoom.GetPowerLevelsAsync();
                await foreach (var member in controlRoomMembers) {
                    if ((member.TypedContent as RoomMemberEventContent)?
                        .Membership == "join" && pls.UserHasTimelinePermission(member.Sender, RoomMessageEventContent.EventId)) admins.Add(member.StateKey);
                }

                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            }
        }, cancellationToken);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

        syncHelper.InviteReceivedHandlers.Add(async Task (args) => {
            var inviteEvent =
                args.Value.InviteState.Events.FirstOrDefault(x =>
                    x.Type == "m.room.member" && x.StateKey == hs.UserId);
            logger.LogInformation("Got invite to {RoomId} by {Sender} with reason: {Reason}", args.Key, inviteEvent!.Sender,
                (inviteEvent.TypedContent as RoomMemberEventContent)!.Reason);
            await _logRoom.SendMessageEventAsync(MessageFormatter.FormatSuccess($"Bot invited to {MessageFormatter.HtmlFormatMention(args.Key)} by {MessageFormatter.HtmlFormatMention(inviteEvent.Sender)}"));
            if (admins.Contains(inviteEvent.Sender)) {
                try {
                    await _logRoom.SendMessageEventAsync(MessageFormatter.FormatSuccess($"Joining {MessageFormatter.HtmlFormatMention(args.Key)}..."));
                    var senderProfile = await hs.GetProfileAsync(inviteEvent.Sender);
                    await hs.GetRoom(args.Key).JoinAsync(reason: $"I was invited by {senderProfile.DisplayName ?? inviteEvent.Sender}!");
                }
                catch (Exception e) {
                    logger.LogError("{}", e.ToString());
                    await _logRoom.SendMessageEventAsync(MessageFormatter.FormatException("Could not join room", e));
                    await hs.GetRoom(args.Key).LeaveAsync(reason: "I was unable to join the room: " + e);
                }
            }
        });

        syncHelper.TimelineEventHandlers.Add(async @event => {
            var room = hs.GetRoom(@event.RoomId);
            try {
                logger.LogInformation(
                    "Got timeline event in {}: {}", @event.RoomId, @event.ToJson(indent: true, ignoreNull: true));

                if (@event != null && (
                        @event.MappedType.IsAssignableTo(typeof(BasePolicy))
                        || @event.MappedType.IsAssignableTo(typeof(PolicyRuleEventContent))
                    )) {
                    await LogPolicyChange(@event);
                    await engine.ReloadActivePolicyListById(@event.RoomId);
                }

                var rules = await engine.GetMatchingPolicies(@event);
                foreach (var matchedRule in rules) {
                    await _logRoom.SendMessageEventAsync(MessageFormatter.FormatSuccessJson(
                        $"{MessageFormatter.HtmlFormatMessageLink(eventId: @event.EventId, roomId: room.RoomId, displayName: "Event")} matched {MessageFormatter.HtmlFormatMessageLink(eventId: @matchedRule.OriginalEvent.EventId, roomId: matchedRule.PolicyList.Room.RoomId, displayName: "rule")}", @matchedRule.OriginalEvent.RawContent));
                }

                if (configuration.DemoMode) {
                    // foreach (var matchedRule in rules) {
                    // await room.SendMessageEventAsync(MessageFormatter.FormatSuccessJson(
                    // $"{MessageFormatter.HtmlFormatMessageLink(eventId: @event.EventId, roomId: room.RoomId, displayName: "Event")} matched {MessageFormatter.HtmlFormatMessageLink(eventId: @matchedRule.EventId, roomId: matchedRule.RoomId, displayName: "rule")}", @matchedRule.RawContent));
                    // }
                    return;
                }
                //
                //                 if (@event is { Type: "m.room.message", TypedContent: RoomMessageEventContent message }) {
                //                     if (message is { MessageType: "m.image" }) {
                //                         //check media
                //                         // var matchedPolicy = await CheckMedia(@event);
                //                         var matchedPolicy = rules.FirstOrDefault();
                //                         if (matchedPolicy is null) return;
                //                         var matchedpolicyData = matchedPolicy.TypedContent as MediaPolicyEventContent;
                //                         await _logRoom.SendMessageEventAsync(
                //                             new RoomMessageEventContent(
                //                                 body:
                //                                 $"User {MessageFormatter.HtmlFormatMention(@event.Sender)} posted an image in {MessageFormatter.HtmlFormatMention(room.RoomId)} that matched rule {matchedPolicy.StateKey}, applying action {matchedpolicyData.Recommendation}, as described in rule: {matchedPolicy.RawContent!.ToJson(ignoreNull: true)}",
                //                                 messageType: "m.text") {
                //                                 Format = "org.matrix.custom.html",
                //                                 FormattedBody =
                //                                     $"<font color=\"#FFFF00\">User {MessageFormatter.HtmlFormatMention(@event.Sender)} posted an image in {MessageFormatter.HtmlFormatMention(room.RoomId)} that matched rule {matchedPolicy.StateKey}, applying action {matchedpolicyData.Recommendation}, as described in rule: <pre>{matchedPolicy.RawContent!.ToJson(ignoreNull: true)}</pre></font>"
                //                             });
                //                         switch (matchedpolicyData.Recommendation) {
                //                             case "warn_admins": {
                //                                 await _controlRoom.SendMessageEventAsync(
                //                                     new RoomMessageEventContent(
                //                                         body: $"{string.Join(' ', admins)}\nUser {MessageFormatter.HtmlFormatMention(@event.Sender)} posted a banned image {message.Url}",
                //                                         messageType: "m.text") {
                //                                         Format = "org.matrix.custom.html",
                //                                         FormattedBody = $"{string.Join(' ', admins.Select(u => MessageFormatter.HtmlFormatMention(u)))}\n" +
                //                                                         $"<font color=\"#FF0000\">User {MessageFormatter.HtmlFormatMention(@event.Sender)} posted a banned image <a href=\"{message.Url}\">{message.Url}</a></font>"
                //                                     });
                //                                 break;
                //                             }
                //                             case "warn": {
                //                                 await room.SendMessageEventAsync(
                //                                     new RoomMessageEventContent(
                //                                         body: $"Please be careful when posting this image: {matchedpolicyData.Reason ?? "No reason specified"}",
                //                                         messageType: "m.text") {
                //                                         Format = "org.matrix.custom.html",
                //                                         FormattedBody =
                //                                             $"<font color=\"#FFFF00\">Please be careful when posting this image: {matchedpolicyData.Reason ?? "No reason specified"}</a></font>"
                //                                     });
                //                                 break;
                //                             }
                //                             case "redact": {
                //                                 await room.RedactEventAsync(@event.EventId, matchedpolicyData.Reason ?? "No reason specified");
                //                                 break;
                //                             }
                //                             case "spoiler": {
                //                                 // <blockquote>
                //                                 //  <a href=\"https://matrix.to/#/@emma:rory.gay\">@emma:rory.gay</a><br>
                //                                 //  <a href=\"https://codeberg.org/crimsonfork/CN\"></a>
                //                                 //  <font color=\"#dc143c\" data-mx-color=\"#dc143c\">
                //                                 //      <b>CN</b>
                //                                 //  </font>:
                //                                 //  <a href=\"https://the-apothecary.club/_matrix/media/v3/download/rory.gay/sLkdxUhipiQaFwRkXcPSRwdg\">test</a><br>
                //                                 //  <span data-mx-spoiler=\"\"><a href=\"https://the-apothecary.club/_matrix/media/v3/download/rory.gay/sLkdxUhipiQaFwRkXcPSRwdg\">
                //                                 //      <img src=\"mxc://rory.gay/sLkdxUhipiQaFwRkXcPSRwdg\" height=\"69\"></a>
                //                                 //  </span>
                //                                 // </blockquote>
                //                                 await room.SendMessageEventAsync(
                //                                     new RoomMessageEventContent(
                //                                         body:
                //                                         $"Please be careful when posting this image: {matchedpolicyData.Reason}, I have spoilered it for you:",
                //                                         messageType: "m.text") {
                //                                         Format = "org.matrix.custom.html",
                //                                         FormattedBody =
                //                                             $"<font color=\"#FFFF00\">Please be careful when posting this image: {matchedpolicyData.Reason}, I have spoilered it for you:</a></font>"
                //                                     });
                //                                 var imageUrl = message.Url;
                //                                 await room.SendMessageEventAsync(
                //                                     new RoomMessageEventContent(body: $"CN: {imageUrl}",
                //                                         messageType: "m.text") {
                //                                         Format = "org.matrix.custom.html",
                //                                         FormattedBody = $"""
                //                                                              <blockquote>
                //                                                                 <font color=\"#dc143c\" data-mx-color=\"#dc143c\">
                //                                                                     <b>CN</b>
                //                                                                 </font>:
                //                                                                 <a href=\"{imageUrl}\">{matchedpolicyData.Reason}</a><br>
                //                                                                 <span data-mx-spoiler=\"\">
                //                                                                     <a href=\"{imageUrl}\">
                //                                                                         <img src=\"{imageUrl}\" height=\"69\">
                //                                                                     </a>
                //                                                                 </span>
                //                                                              </blockquote>
                //                                                          """
                //                                     });
                //                                 await room.RedactEventAsync(@event.EventId, "Automatically spoilered: " + matchedpolicyData.Reason);
                //                                 break;
                //                             }
                //                             case "mute": {
                //                                 await room.RedactEventAsync(@event.EventId, matchedpolicyData.Reason);
                //                                 //change powerlevel to -1
                //                                 var currentPls = await room.GetPowerLevelsAsync();
                //                                 if (currentPls is null) {
                //                                     logger.LogWarning("Unable to get power levels for {room}", room.RoomId);
                //                                     await _logRoom.SendMessageEventAsync(
                //                                         MessageFormatter.FormatError($"Unable to get power levels for {MessageFormatter.HtmlFormatMention(room.RoomId)}"));
                //                                     return;
                //                                 }
                //
                //                                 currentPls.Users ??= new();
                //                                 currentPls.Users[@event.Sender] = -1;
                //                                 await room.SendStateEventAsync("m.room.power_levels", currentPls);
                //                                 break;
                //                             }
                //                             case "kick": {
                //                                 await room.RedactEventAsync(@event.EventId, matchedpolicyData.Reason);
                //                                 await room.KickAsync(@event.Sender, matchedpolicyData.Reason);
                //                                 break;
                //                             }
                //                             case "ban": {
                //                                 await room.RedactEventAsync(@event.EventId, matchedpolicyData.Reason);
                //                                 await room.BanAsync(@event.Sender, matchedpolicyData.Reason);
                //                                 break;
                //                             }
                //                             default: {
                //                                 throw new ArgumentOutOfRangeException("recommendation",
                //                                     $"Unknown response type {matchedpolicyData.Recommendation}!");
                //                             }
                //                         }
                //                     }
                //                 }
            }
            catch (Exception e) {
                logger.LogError("{}", e.ToString());
                await _controlRoom.SendMessageEventAsync(
                    MessageFormatter.FormatException($"Unable to process event in {MessageFormatter.HtmlFormatMention(room.RoomId)}", e));
                await _logRoom.SendMessageEventAsync(
                    MessageFormatter.FormatException($"Unable to process event in {MessageFormatter.HtmlFormatMention(room.RoomId)}", e));
                await using var stream = new MemoryStream(e.ToString().AsBytes().ToArray());
                await _controlRoom.SendFileAsync("error.log.cs", stream);
                await _logRoom.SendFileAsync("error.log.cs", stream);
            }
        });
        await engine.ReloadActivePolicyLists();
        await syncHelper.RunSyncLoopAsync();
    }

    private async Task LogPolicyChange(StateEventResponse changeEvent) {
        var room = hs.GetRoom(changeEvent.RoomId!);
        var message = MessageFormatter.FormatWarning($"Policy change detected in {MessageFormatter.HtmlFormatMessageLink(changeEvent.RoomId, changeEvent.EventId, [hs.ServerName], await room.GetNameOrFallbackAsync())}!");
        message = message.ConcatLine(new RoomMessageEventContent(body: $"Policy type: {changeEvent.Type} -> {changeEvent.MappedType.Name}") {
            FormattedBody = $"Policy type: {changeEvent.Type} -> {changeEvent.MappedType.Name}"
        });
        var isUpdated = changeEvent.Unsigned.PrevContent is { Count: > 0 };
        var isRemoved = changeEvent.RawContent is not { Count: > 0 };
        // if (isUpdated) {
        //     message = message.ConcatLine(MessageFormatter.FormatSuccess("Rule updated!"));
        //     message = message.ConcatLine(MessageFormatter.FormatSuccessJson("Old rule:", changeEvent.Unsigned.PrevContent!));
        // }
        // else if (isRemoved) {
        //     message = message.ConcatLine(MessageFormatter.FormatWarningJson("Rule removed!", changeEvent.Unsigned.PrevContent!));
        // }
        // else {
        //     message = message.ConcatLine(MessageFormatter.FormatSuccess("New rule added!"));
        // }
        message = message.ConcatLine(MessageFormatter.FormatSuccessJson($"{(isUpdated ? "Updated" : isRemoved ? "Removed" : "New")} rule: {changeEvent.StateKey}", changeEvent.RawContent!));
        if (isRemoved || isUpdated) {
            message = message.ConcatLine(MessageFormatter.FormatSuccessJson("Old content: ", changeEvent.Unsigned.PrevContent!));
        }
        
        await _logRoom.SendMessageEventAsync(message);
    }

    /// <summary>Triggered when the application host is performing a graceful shutdown.</summary>
    /// <param name="cancellationToken">Indicates that the shutdown process should no longer be graceful.</param>
    public async Task StopAsync(CancellationToken cancellationToken) {
        logger.LogInformation("Shutting down bot!");
    }

}
