using LibMatrix.EventTypes.Spec;
using LibMatrix.Helpers;
using LibMatrix.RoomTypes;
using LibMatrix.Services;
using LibMatrix.Utilities.Bot.Interfaces;
using ModerationBot.AccountData;

namespace ModerationBot.Commands;

public class DbgAllRoomsArePolicyListsCommand
    (IServiceProvider services, HomeserverProviderService hsProvider, HomeserverResolverService hsResolver, PolicyEngine engine) : ICommand {
    public string Name { get; } = "dbg-allroomsarepolicy";
    public string Description { get; } = "[Debug] mark all rooms as trusted policy rooms";
    private GenericRoom logRoom { get; set; }

    public async Task<bool> CanInvoke(CommandContext ctx) {
#if !DEBUG
        return false;
#endif

        //check if user is admin in control room
        var botData = await ctx.Homeserver.GetAccountDataAsync<BotData>("gay.rory.moderation_bot_data");
        var controlRoom = ctx.Homeserver.GetRoom(botData.ControlRoom);
        var isAdmin = (await controlRoom.GetPowerLevelsAsync())!.UserHasStatePermission(ctx.MessageEvent.Sender, "m.room.ban");
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

        var joinedRooms = await ctx.Homeserver.GetJoinedRooms();

        await ctx.Homeserver.SetAccountDataAsync("gay.rory.moderation_bot.policy_lists", joinedRooms.ToDictionary(x => x.RoomId, x => new PolicyList() {
            Trusted = true
        }));

        await engine.ReloadActivePolicyLists();
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
