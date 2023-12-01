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

public class DbgDumpActivePoliciesCommand
    (IServiceProvider services, HomeserverProviderService hsProvider, HomeserverResolverService hsResolver, PolicyEngine engine) : ICommand {
    public string Name { get; } = "dbg-dumppolicies";
    public string Description { get; } = "[Debug] Dump all active policies";
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
        await ctx.Room.SendFileAsync("all.json", new MemoryStream(engine.ActivePolicies.ToJson().AsBytes().ToArray()), contentType: "application/json");
        await ctx.Room.SendFileAsync("by-type.json", new MemoryStream(engine.ActivePoliciesByType.ToJson().AsBytes().ToArray()), contentType: "application/json");
    }
}
