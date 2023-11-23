using System.Buffers.Text;
using System.Security.Cryptography;
using ArcaneLibs.Extensions;
using LibMatrix;
using LibMatrix.EventTypes.Spec;
using LibMatrix.Helpers;
using LibMatrix.Services;
using LibMatrix.Utilities.Bot.Interfaces;
using ModerationBot.AccountData;
using ModerationBot.StateEventTypes;
using ModerationBot.StateEventTypes.Policies.Implementations;

namespace ModerationBot.Commands;

public class BanMediaCommand(IServiceProvider services, HomeserverProviderService hsProvider, HomeserverResolverService hsResolver, PolicyEngine engine) : ICommand {
    public string Name { get; } = "banmedia";
    public string Description { get; } = "Create a policy banning a piece of media, must be used in reply to a message";

    public async Task<bool> CanInvoke(CommandContext ctx) {
        //check if user is admin in control room
        var botData = await ctx.Homeserver.GetAccountDataAsync<BotData>("gay.rory.moderation_bot_data");
        var controlRoom = ctx.Homeserver.GetRoom(botData.ControlRoom);
        var isAdmin = (await controlRoom.GetPowerLevelsAsync())!.UserHasPermission(ctx.MessageEvent.Sender, "m.room.ban");
        if (!isAdmin) {
            // await ctx.Reply("You do not have permission to use this command!");
            await ctx.Homeserver.GetRoom(botData.LogRoom!).SendMessageEventAsync(
                new RoomMessageEventContent(body: $"User {ctx.MessageEvent.Sender} tried to use command {Name} but does not have permission!", messageType: "m.text"));
        }

        return isAdmin;
    }

    public async Task Invoke(CommandContext ctx) {
        
        var botData = await ctx.Homeserver.GetAccountDataAsync<BotData>("gay.rory.moderation_bot_data");
        var policyRoom = ctx.Homeserver.GetRoom(botData.DefaultPolicyRoom ?? botData.ControlRoom);
        var logRoom = ctx.Homeserver.GetRoom(botData.LogRoom ?? botData.ControlRoom);

        //check if reply
        var messageContent = ctx.MessageEvent.TypedContent as RoomMessageEventContent;
        if (messageContent?.RelatesTo is { InReplyTo: not null }) {
            try {
                await logRoom.SendMessageEventAsync(
                    new RoomMessageEventContent(
                        body: $"User {MessageFormatter.HtmlFormatMention(ctx.MessageEvent.Sender)} is trying to ban media {messageContent!.RelatesTo!.InReplyTo!.EventId}",
                        messageType: "m.text"));

                //get replied message
                var repliedMessage = await ctx.Room.GetEventAsync<StateEventResponse>(messageContent.RelatesTo!.InReplyTo!.EventId);

                //check if recommendation is in list
                if (ctx.Args.Length < 2) {
                    await ctx.Room.SendMessageEventAsync(MessageFormatter.FormatError("You must specify a recommendation type and reason!"));
                    return;
                }

                var recommendation = ctx.Args[0];

                if (recommendation is not ("ban" or "kick" or "mute" or "redact" or "spoiler" or "warn" or "warn_admins")) {
                    await ctx.Room.SendMessageEventAsync(
                        MessageFormatter.FormatError(
                            $"Invalid recommendation type {recommendation}, must be `warn_admins`, `warn`, `spoiler`, `redact`, `mute`, `kick` or `ban`!"));
                    return;
                }

                //hash file
                var mxcUri = (repliedMessage.TypedContent as RoomMessageEventContent).Url!;
                var resolvedUri = await hsResolver.ResolveMediaUri(mxcUri.Split('/')[2], mxcUri);
                var hashAlgo = SHA3_256.Create();
                var uriHash = hashAlgo.ComputeHash(mxcUri.AsBytes().ToArray());
                byte[]? fileHash = null;

                try {
                    fileHash = await hashAlgo.ComputeHashAsync(await ctx.Homeserver.ClientHttpClient.GetStreamAsync(resolvedUri));
                }
                catch (Exception ex) {
                    await logRoom.SendMessageEventAsync(
                        MessageFormatter.FormatException($"Error calculating file hash for {mxcUri} via {mxcUri.Split('/')[2]}, retrying via {ctx.Homeserver.BaseUrl}...",
                            ex));
                    try {
                        resolvedUri = await hsResolver.ResolveMediaUri(ctx.Homeserver.BaseUrl, mxcUri);
                        fileHash = await hashAlgo.ComputeHashAsync(await ctx.Homeserver.ClientHttpClient.GetStreamAsync(resolvedUri));
                    }
                    catch (Exception ex2) {
                        await ctx.Room.SendMessageEventAsync(MessageFormatter.FormatException("Error calculating file hash", ex2));
                        await logRoom.SendMessageEventAsync(
                            MessageFormatter.FormatException($"Error calculating file hash via {ctx.Homeserver.BaseUrl}!", ex2));
                    }
                }

                MediaPolicyFile policy;
                await policyRoom.SendStateEventAsync("gay.rory.moderation.rule.media", Guid.NewGuid().ToString(), policy = new MediaPolicyFile {
                    Entity = Convert.ToBase64String(uriHash),
                    FileHash = Convert.ToBase64String(fileHash),
                    Reason = string.Join(' ', ctx.Args[1..]),
                    Recommendation = recommendation,
                });

                await ctx.Room.SendMessageEventAsync(MessageFormatter.FormatSuccessJson("Media policy created", policy));
                await logRoom.SendMessageEventAsync(MessageFormatter.FormatSuccessJson("Media policy created", policy));
            }
            catch (Exception e) {
                await logRoom.SendMessageEventAsync(MessageFormatter.FormatException("Error creating policy", e));
                await ctx.Room.SendMessageEventAsync(MessageFormatter.FormatException("Error creating policy", e));
                await using var stream = new MemoryStream(e.ToString().AsBytes().ToArray());
                await logRoom.SendFileAsync("error.log.cs", stream);
            }
        }
        else {
            await ctx.Room.SendMessageEventAsync(MessageFormatter.FormatError("This command must be used in reply to a message!"));
        }
    }
}
