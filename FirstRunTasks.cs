using LibMatrix;
using LibMatrix.Homeservers;
using LibMatrix.Responses;
using ModerationBot.AccountData;

namespace ModerationBot;

public class FirstRunTasks {
    public static async Task<BotData> ConstructBotData(AuthenticatedHomeserverGeneric hs, ModerationBotConfiguration configuration, BotData? botdata) {
        botdata ??= new BotData();
        var creationContent = CreateRoomRequest.CreatePrivate(hs, name: "Rory&::ModerationBot - Control room", roomAliasName: "moderation-bot-control-room");
        creationContent.Invite = configuration.Admins;
        creationContent.CreationContent["type"] = "gay.rory.moderation_bot.control_room";

        if (botdata.ControlRoom is null)
            try {
                botdata.ControlRoom = (await hs.CreateRoom(creationContent)).RoomId;
            }
            catch (Exception e) {
                if (e is not MatrixException { ErrorCode: "M_ROOM_IN_USE" }) {
                    Console.WriteLine(e);
                    throw;
                }

                creationContent.RoomAliasName += $"-{Guid.NewGuid()}";
                botdata.ControlRoom = (await hs.CreateRoom(creationContent)).RoomId;
            }
        //set access rules to allow joining via control room
        // creationContent.InitialState.Add(new StateEvent {
        //     Type = "m.room.join_rules",
        //     StateKey = "",
        //     TypedContent = new RoomJoinRulesEventContent {
        //         JoinRule = "knock_restricted",
        //         Allow = new() {
        //             new RoomJoinRulesEventContent.AllowEntry {
        //                 Type = "m.room_membership",
        //                 RoomId = botdata.ControlRoom
        //             }
        //         }
        //     }
        // });

        creationContent.Name = "Rory&::ModerationBot - Log room";
        creationContent.RoomAliasName = "moderation-bot-log-room";
        creationContent.CreationContent["type"] = "gay.rory.moderation_bot.log_room";

        if (botdata.LogRoom is null)
            try {
                botdata.LogRoom = (await hs.CreateRoom(creationContent)).RoomId;
            }
            catch (Exception e) {
                if (e is not MatrixException { ErrorCode: "M_ROOM_IN_USE" }) {
                    Console.WriteLine(e);
                    throw;
                }

                creationContent.RoomAliasName += $"-{Guid.NewGuid()}";
                botdata.LogRoom = (await hs.CreateRoom(creationContent)).RoomId;
            }

        creationContent.Name = "Rory&::ModerationBot - Policy room";
        creationContent.RoomAliasName = "moderation-bot-policy-room";
        creationContent.CreationContent["type"] = "gay.rory.moderation_bot.policy_room";

        if (botdata.DefaultPolicyRoom is null)
            try {
                botdata.DefaultPolicyRoom = (await hs.CreateRoom(creationContent)).RoomId;
            }
            catch (Exception e) {
                if (e is not MatrixException { ErrorCode: "M_ROOM_IN_USE" }) {
                    Console.WriteLine(e);
                    throw;
                }

                creationContent.RoomAliasName += $"-{Guid.NewGuid()}";
                botdata.DefaultPolicyRoom = (await hs.CreateRoom(creationContent)).RoomId;
            }

        await hs.SetAccountDataAsync("gay.rory.moderation_bot_data", botdata);

        return botdata;
    }
}
