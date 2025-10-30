using System.Diagnostics.CodeAnalysis;
using LibMatrix.Homeservers;
using LibMatrix.Responses;
using LibMatrix.RoomTypes;
using ModerationBot.AccountData;

namespace ModerationBot.Services;

public class ModerationBotRoomProvider(AuthenticatedHomeserverGeneric hs, ModerationBotConfiguration cfg) {
    private BotData? _botData;

    public BotData? BotData {
        get {
            if (BotDataExpiry >= DateTime.UtcNow) return _botData;
            Console.WriteLine("BotData expired!");
            return null;
        }
        set {
            _botData = value;
            Console.WriteLine("BotData updated!");
            BotDataExpiry = DateTime.UtcNow.AddMinutes(5);
        }
    }

    private DateTime BotDataExpiry { get; set; }
    
    [MemberNotNull(nameof(BotData))]
    private async Task<BotData> GetBotDataAsync() {
        try {
            BotData ??= await hs.GetAccountDataAsync<BotData>(BotData.EventId);
        }
        catch (Exception e) {
            Console.WriteLine(e);
            await hs.SetAccountDataAsync(BotData.EventId, new BotData());
            return await GetBotDataAsync();
        }

        if (BotData == null)
            throw new NullReferenceException("BotData is null!");

        return BotData;
    }
    
    public async Task<GenericRoom> GetControlRoomAsync() {
        var botData = await GetBotDataAsync();
        if (botData.ControlRoom == null) {
            var createRoomRequest = CreateRoomRequest.CreatePrivate(hs, "Rory&::ModerationBot - Control Room");
            createRoomRequest.Invite = cfg.Admins;
            var newRoom = await hs.CreateRoom(createRoomRequest, true, true, true);
            BotData.ControlRoom = newRoom.RoomId;
            await hs.SetAccountDataAsync(BotData.EventId, BotData);
        }

        return hs.GetRoom(BotData.ControlRoom!);
    }

    public async Task<GenericRoom> GetLogRoomAsync() {
        var botData = await GetBotDataAsync();
        if (botData.LogRoom == null) {
            var controlRoom = await GetControlRoomAsync();
            var createRoomRequest = CreateRoomRequest.CreatePrivate(hs, "Rory&::ModerationBot - Log Room");
            createRoomRequest.Invite = (await controlRoom.GetMembersListAsync()).Select(x=>x.StateKey).ToList();
            var newRoom = await hs.CreateRoom(createRoomRequest, true, true, true);
            BotData.LogRoom = newRoom.RoomId;
            await hs.SetAccountDataAsync(BotData.EventId, BotData);
        }

        return hs.GetRoom(BotData.LogRoom!);
    }
    
    public async Task<GenericRoom?> GetDefaultPolicyRoomAsync() {
        var botData = await GetBotDataAsync();

        return string.IsNullOrWhiteSpace(botData.DefaultPolicyRoom) ? null : hs.GetRoom(BotData.DefaultPolicyRoom!);
    }
}