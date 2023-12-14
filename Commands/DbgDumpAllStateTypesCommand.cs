using ArcaneLibs.Extensions;
using LibMatrix;
using LibMatrix.EventTypes.Spec;
using LibMatrix.RoomTypes;
using LibMatrix.Services;
using LibMatrix.Utilities.Bot.Interfaces;
using ModerationBot.AccountData;

namespace ModerationBot.Commands;

public class DbgDumpAllStateTypesCommand
    (IServiceProvider services, HomeserverProviderService hsProvider, HomeserverResolverService hsResolver, PolicyEngine engine) : ICommand {
    public string Name { get; } = "dbg-dumpstatetypes";
    public string Description { get; } = "[Debug] Dump all state types we can find";
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

        var tasks = joinedRooms.Select(GetStateTypes).ToAsyncEnumerable();
        await foreach (var (room, (raw, html)) in tasks) {
            await ctx.Room.SendMessageEventAsync(new RoomMessageEventContent("m.text") {
                Body = $"States for {room.RoomId}:\n{raw}",
                FormattedBody = $"States for {room.RoomId}:\n{html}",
                Format = "org.matrix.custom.html"
            });
        }
    }

    private async Task<(GenericRoom room, (string raw, string html))> GetStateTypes(GenericRoom memberRoom) {
        var states = await memberRoom.GetFullStateAsListAsync();

        return (memberRoom, SummariseStateTypeCounts(states));
    }

    private static (string Raw, string Html) SummariseStateTypeCounts(IList<StateEventResponse> states) {
        string raw = "Count | State type | Mapped type", html = "<table><tr><th>Count</th><th>State type</th><th>Mapped type</th></tr>";
        var groupedStates = states.GroupBy(x => x.Type).ToDictionary(x => x.Key, x => x.ToList()).OrderByDescending(x => x.Value.Count);
        foreach (var (type, stateGroup) in groupedStates) {
            raw += $"{stateGroup.Count} | {type} | {stateGroup[0].GetType.Name}";
            html += $"<tr><td>{stateGroup.Count}</td><td>{type}</td><td>{stateGroup[0].GetType.Name}</td></tr>";
        }

        html += "</table>";
        return (raw, html);
    }
}
