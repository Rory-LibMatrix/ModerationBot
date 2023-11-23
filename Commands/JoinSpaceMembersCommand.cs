using System.Buffers.Text;
using System.Security.Cryptography;
using ArcaneLibs.Extensions;
using LibMatrix;
using LibMatrix.EventTypes.Spec;
using LibMatrix.Helpers;
using LibMatrix.RoomTypes;
using LibMatrix.Services;
using LibMatrix.Utilities.Bot.Interfaces;
using ModerationBot.AccountData;
using ModerationBot.StateEventTypes;

namespace ModerationBot.Commands;

public class JoinSpaceMembersCommand(IServiceProvider services, HomeserverProviderService hsProvider, HomeserverResolverService hsResolver, PolicyEngine engine) : ICommand {
    public string Name { get; } = "joinspacemembers";
    public string Description { get; } = "Join all rooms in space";
    private GenericRoom logRoom { get; set; }

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
        logRoom = ctx.Homeserver.GetRoom(botData.LogRoom ?? botData.ControlRoom);

        await logRoom.SendMessageEventAsync(MessageFormatter.FormatSuccess($"Joining space children of {ctx.Args[0]} with reason: {string.Join(' ', ctx.Args[1..])}"));
        var roomId = ctx.Args[0];
        var servers = new List<string>() {ctx.Homeserver.ServerName};
        if (roomId.StartsWith('[')) {
            
        }

        if (roomId.StartsWith('#')) {
            var res = await ctx.Homeserver.ResolveRoomAliasAsync(roomId);
            roomId = res.RoomId;
            servers.AddRange(servers);
        }

        var room = ctx.Homeserver.GetRoom(roomId);
        var tasks = new List<Task<bool>>();
        await foreach (var memberRoom in room.AsSpace.GetChildrenAsync()) {
            servers.Add(room.RoomId.Split(':', 2)[1]);
            servers = servers.Distinct().ToList();
            tasks.Add(JoinRoom(memberRoom, string.Join(' ', ctx.Args[1..]), servers));
        }

        await foreach (var b in tasks.ToAsyncEnumerable()) {
            await Task.Delay(50);
        }
    }

    private async Task<bool> JoinRoom(GenericRoom memberRoom, string reason, List<string> servers) {
        try {
            await memberRoom.JoinAsync(servers.ToArray(), reason);
            await logRoom.SendMessageEventAsync(MessageFormatter.FormatSuccess($"Joined room {memberRoom.RoomId}"));
        }
        catch (Exception e) {
            await logRoom.SendMessageEventAsync(MessageFormatter.FormatException($"Failed to join {memberRoom.RoomId}", e));
        }

        return true;
    }
}
