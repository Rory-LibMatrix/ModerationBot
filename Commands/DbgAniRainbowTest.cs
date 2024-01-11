using System.Diagnostics;
using LibMatrix.EventTypes.Spec;
using LibMatrix.Helpers;
using LibMatrix.RoomTypes;
using LibMatrix.Services;
using LibMatrix.Utilities.Bot.Interfaces;
using ModerationBot.AccountData;

namespace ModerationBot.Commands;

public class DbgAniRainbowTest(IServiceProvider services, HomeserverProviderService hsProvider, HomeserverResolverService hsResolver, PolicyEngine engine) : ICommand {
    public string Name { get; } = "dbg-ani-rainbow";
    public string Description { get; } = "[Debug] animated rainbow :)";
    private GenericRoom logRoom { get; set; }

    public async Task<bool> CanInvoke(CommandContext ctx) {
        return ctx.Room.RoomId == "!DoHEdFablOLjddKWIp:rory.gay";
    }

    public async Task Invoke(CommandContext ctx) {
        //255 long string
        // var rainbow = "🟥🟧🟨🟩🟦🟪";
        var rainbow = "M";
        var chars = rainbow;
        for (var i = 0; i < 76; i++) {
            chars += rainbow[i%rainbow.Length];
        }

        var msg = new MessageBuilder(msgType: "m.notice").WithRainbowString(chars).Build();
        var msgEvent = await ctx.Room.SendMessageEventAsync(msg);
        
        Task.Run(async () => {

            int i = 0;
            while (true) {
                msg = new MessageBuilder(msgType: "m.notice").WithRainbowString(chars, offset: i+=5).Build();
                    // .SetReplaceRelation<RoomMessageEventContent>(msgEvent.EventId);
                // msg.Body = "";
                // msg.FormattedBody = "";
                var sw = Stopwatch.StartNew();
                await ctx.Room.SendMessageEventAsync(msg);
                await Task.Delay(sw.Elapsed);
            }
            
        });

    }
}