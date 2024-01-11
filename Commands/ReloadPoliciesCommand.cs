using LibMatrix.EventTypes.Spec;
using LibMatrix.Helpers;
using LibMatrix.Services;
using LibMatrix.Utilities.Bot.Interfaces;
using ModerationBot.AccountData;

namespace ModerationBot.Commands;

public class ReloadPoliciesCommand(IServiceProvider services, HomeserverProviderService hsProvider, HomeserverResolverService hsResolver, PolicyEngine engine) : ICommand {
    public string Name { get; } = "reloadpolicies";
    public string Description { get; } = "Reload policies";

    public async Task<bool> CanInvoke(CommandContext ctx) {
        if (ctx.MessageEvent.Sender == "@cadence:cadence.moe") return true;
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
        var policyRoom = ctx.Homeserver.GetRoom(botData.DefaultPolicyRoom ?? botData.ControlRoom);
        var logRoom = ctx.Homeserver.GetRoom(botData.LogRoom ?? botData.ControlRoom);
        
        await logRoom.SendMessageEventAsync(MessageFormatter.FormatSuccess($"Reloading policy lists due to manual invocation!!!!"));
        await engine.ReloadActivePolicyLists();
    }
}
