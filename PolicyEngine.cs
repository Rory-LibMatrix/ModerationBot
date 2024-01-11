using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ArcaneLibs.Extensions;
using LibMatrix;
using LibMatrix.EventTypes.Spec;
using LibMatrix.EventTypes.Spec.State.Policy;
using LibMatrix.Helpers;
using LibMatrix.Homeservers;
using LibMatrix.RoomTypes;
using LibMatrix.Services;
using Microsoft.Extensions.Logging;
using ModerationBot.AccountData;
using ModerationBot.StateEventTypes.Policies;
using ModerationBot.StateEventTypes.Policies.Implementations;

namespace ModerationBot;

public class PolicyEngine(AuthenticatedHomeserverGeneric hs, ILogger<ModerationBot> logger, ModerationBotConfiguration configuration, HomeserverResolverService hsResolver) {
    private Dictionary<string, PolicyList> PolicyListAccountData { get; set; } = new();
    // ReSharper disable once MemberCanBePrivate.Global
    public List<PolicyList> ActivePolicyLists { get; set; } = new();
    public List<BasePolicy> ActivePolicies { get; set; } = new();
    public Dictionary<string, List<BasePolicy>> ActivePoliciesByType { get; set; } = new();
    private GenericRoom? _logRoom;
    private GenericRoom? _controlRoom;

    public async Task ReloadActivePolicyLists() {
        var sw = Stopwatch.StartNew();

        var botData = await hs.GetAccountDataAsync<BotData>("gay.rory.moderation_bot_data");
        _logRoom ??= hs.GetRoom(botData.LogRoom ?? botData.ControlRoom);
        _controlRoom ??= hs.GetRoom(botData.ControlRoom);

        await _controlRoom?.SendMessageEventAsync(MessageFormatter.FormatSuccess("Reloading policy lists!"))!;
        await _logRoom?.SendMessageEventAsync(MessageFormatter.FormatSuccess("Reloading policy lists!"))!;

        var progressMessage = await _logRoom?.SendMessageEventAsync(MessageFormatter.FormatSuccess("0/? policy lists loaded"))!;

        var policyLists = new List<PolicyList>();
        try {
            PolicyListAccountData = await hs.GetAccountDataAsync<Dictionary<string, PolicyList>>("gay.rory.moderation_bot.policy_lists");
        }
        catch (MatrixException e) {
            if (e is not { ErrorCode: "M_NOT_FOUND" }) throw;
        }

        if (!PolicyListAccountData.ContainsKey(botData.DefaultPolicyRoom!)) {
            PolicyListAccountData.Add(botData.DefaultPolicyRoom!, new PolicyList() {
                Trusted = true
            });
            await hs.SetAccountDataAsync("gay.rory.moderation_bot.policy_lists", PolicyListAccountData);
        }

        var loadTasks = new List<Task<PolicyList>>();
        foreach (var (roomId, policyList) in PolicyListAccountData) {
            var room = hs.GetRoom(roomId);
            loadTasks.Add(LoadPolicyListAsync(room, policyList));
        }

        await foreach (var policyList in loadTasks.ToAsyncEnumerable()) {
            policyLists.Add(policyList);

            if (false || policyList.Policies.Count >= 256 || policyLists.Count == PolicyListAccountData.Count) {
                var progressMsgContent = MessageFormatter.FormatSuccess($"{policyLists.Count}/{PolicyListAccountData.Count} policy lists loaded, " +
                                                                        $"{policyLists.Sum(x => x.Policies.Count)} policies total, {sw.Elapsed} elapsed.")
                    .SetReplaceRelation<RoomMessageEventContent>(progressMessage.EventId);

                await _logRoom?.SendMessageEventAsync(progressMsgContent);
            }
        }

        // Console.WriteLine($"Reloaded policy list data in {sw.Elapsed}");
        // await _logRoom.SendMessageEventAsync(MessageFormatter.FormatSuccess($"Done fetching {policyLists.Count} policy lists in {sw.Elapsed}!"));

        ActivePolicyLists = policyLists;
        ActivePolicies = await GetActivePolicies();
    }

    private async Task<PolicyList> LoadPolicyListAsync(GenericRoom room, PolicyList policyList) {
        policyList.Room = room;
        policyList.Policies.Clear();

        var stateEvents = room.GetFullStateAsync();
        await foreach (var stateEvent in stateEvents) {
            if (stateEvent != null && (
                    stateEvent.MappedType.IsAssignableTo(typeof(BasePolicy))
                    || stateEvent.MappedType.IsAssignableTo(typeof(PolicyRuleEventContent))
                )) {
                policyList.Policies.Add(stateEvent);
            }
        }

        // if (policyList.Policies.Count >= 1)
        // await _logRoom?.SendMessageEventAsync(
        // MessageFormatter.FormatSuccess($"Loaded {policyList.Policies.Count} policies for {MessageFormatter.HtmlFormatMention(room.RoomId)}!"))!;

        return policyList;
    }


    public async Task ReloadActivePolicyListById(string roomId) {
        if (!ActivePolicyLists.Any(x => x.Room.RoomId == roomId)) return;
        await LoadPolicyListAsync(hs.GetRoom(roomId), ActivePolicyLists.Single(x => x.Room.RoomId == roomId));
        ActivePolicies = await GetActivePolicies();
    }

    public async Task<List<BasePolicy>> GetActivePolicies() {
        var sw = Stopwatch.StartNew();
        List<BasePolicy> activePolicies = new();

        foreach (var activePolicyList in ActivePolicyLists) {
            foreach (var policyEntry in activePolicyList.Policies) {
                // TODO: implement rule translation
                var policy = policyEntry.TypedContent is BasePolicy ? policyEntry.TypedContent as BasePolicy : policyEntry.RawContent.Deserialize<UnknownPolicy>();
                if (policy?.Entity is null) continue;
                policy.PolicyList = activePolicyList;
                policy.OriginalEvent = policyEntry;
                activePolicies.Add(policy);
            }
        }

        Console.WriteLine($"Translated policy list data in {sw.Elapsed}");
        ActivePoliciesByType = activePolicies.GroupBy(x => x.GetType().Name).ToDictionary(x => x.Key, x => x.ToList());
        await _logRoom.SendMessageEventAsync(MessageFormatter.FormatSuccess($"Translated policy list data in {sw.GetElapsedAndRestart()}"));
        // await _logRoom.SendMessageEventAsync(MessageFormatter.FormatSuccess($"Built policy type map in {sw.GetElapsedAndRestart()}"));

        var summary = SummariseStateTypeCounts(activePolicies.Select(x => x.OriginalEvent).ToList());
        await _logRoom?.SendMessageEventAsync(new RoomMessageEventContent() {
            Body = summary.Raw,
            FormattedBody = summary.Html,
            Format = "org.matrix.custom.html"
        })!;

        return activePolicies;
    }

    public async Task<List<BasePolicy>> GetMatchingPolicies(StateEventResponse @event) {
        List<BasePolicy> matchingPolicies = new();
        if (@event.Sender == @hs.UserId) return matchingPolicies; //ignore self at all costs

        if (ActivePoliciesByType.TryGetValue(nameof(ServerPolicyRuleEventContent), out var serverPolicies)) {
            var userServer = @event.Sender.Split(':', 2)[1];
            matchingPolicies.AddRange(serverPolicies.Where(x => x.Entity == userServer));
        }

        if (ActivePoliciesByType.TryGetValue(nameof(UserPolicyRuleEventContent), out var userPolicies)) {
            matchingPolicies.AddRange(userPolicies.Where(x => x.Entity == @event.Sender));
        }

        if (@event.TypedContent is RoomMessageEventContent msgContent) {
            matchingPolicies.AddRange(await CheckMessageContent(@event));
            // if (msgContent.MessageType == "m.text" || msgContent.MessageType == "m.notice") ; //TODO: implement word etc. filters
            if (msgContent.MessageType == "m.image" || msgContent.MessageType == "m.file" || msgContent.MessageType == "m.audio" || msgContent.MessageType == "m.video")
                matchingPolicies.AddRange(await CheckMedia(@event));
        }

        return matchingPolicies;
    }

    #region Policy matching

    private async Task<List<BasePolicy>> CheckMessageContent(StateEventResponse @event) {
        var matchedRules = new List<BasePolicy>();
        var msgContent = @event.TypedContent as RoomMessageEventContent;

        if (ActivePoliciesByType.TryGetValue(nameof(MessagePolicyContainsText), out var messageContainsPolicies))
            foreach (var policy in messageContainsPolicies) {
                if ((@msgContent?.Body?.ToLowerInvariant().Contains(policy.Entity.ToLowerInvariant()) ?? false) || (@msgContent?.FormattedBody?.ToLowerInvariant().Contains(policy.Entity.ToLowerInvariant()) ?? false))
                    matchedRules.Add(policy);
            }


        return matchedRules;
    }

    private async Task<List<BasePolicy>> CheckMedia(StateEventResponse @event) {
        var matchedRules = new List<BasePolicy>();
        var hashAlgo = SHA3_256.Create();

        var mxcUri = @event.RawContent["url"].GetValue<string>();

        //check server policies before bothering with hashes
        if (ActivePoliciesByType.TryGetValue(nameof(MediaPolicyHomeserver), out var mediaHomeserverPolicies))
            foreach (var policy in mediaHomeserverPolicies) {
                logger.LogInformation("Checking rule {rule}: {data}", policy.OriginalEvent.StateKey, policy.OriginalEvent.TypedContent.ToJson(ignoreNull: true, indent: false));
                policy.Entity = policy.Entity.Replace("\\*", ".*").Replace("\\?", ".");
                var regex = new Regex($"mxc://({policy.Entity})/.*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
                if (regex.IsMatch(@event.RawContent["url"]!.GetValue<string>())) {
                    logger.LogInformation("{url} matched rule {rule}", @event.RawContent["url"], policy.ToJson(ignoreNull: true));
                    matchedRules.Add(policy);
                    // continue;
                }
            }

        var resolvedUri = await hsResolver.ResolveMediaUri(mxcUri.Split('/')[2], mxcUri);
        var uriHash = hashAlgo.ComputeHash(mxcUri.AsBytes().ToArray());
        byte[]? fileHash = null;

        try {
            fileHash = await hashAlgo.ComputeHashAsync(await hs.ClientHttpClient.GetStreamAsync(resolvedUri));
        }
        catch (Exception ex) {
            await _logRoom.SendMessageEventAsync(
                MessageFormatter.FormatException($"Error calculating file hash for {mxcUri} via {mxcUri.Split('/')[2]} ({resolvedUri}), retrying via {hs.BaseUrl}...",
                    ex));
            try {
                resolvedUri = await hsResolver.ResolveMediaUri(hs.BaseUrl, mxcUri);
                fileHash = await hashAlgo.ComputeHashAsync(await hs.ClientHttpClient.GetStreamAsync(resolvedUri));
            }
            catch (Exception ex2) {
                await _logRoom.SendMessageEventAsync(
                    MessageFormatter.FormatException($"Error calculating file hash via {hs.BaseUrl} ({resolvedUri})!", ex2));
            }
        }

        logger.LogInformation("Checking media {url} with hash {hash}", resolvedUri, fileHash);

        if (ActivePoliciesByType.ContainsKey(nameof(MediaPolicyFile)))
            foreach (MediaPolicyFile policy in ActivePoliciesByType[nameof(MediaPolicyFile)]) {
                logger.LogInformation("Checking rule {rule}: {data}", policy.OriginalEvent.StateKey, policy.OriginalEvent.TypedContent.ToJson(ignoreNull: true, indent: false));
                if (policy.Entity is not null && Convert.ToBase64String(uriHash).SequenceEqual(policy.Entity)) {
                    logger.LogInformation("{url} matched rule {rule} by uri hash", @event.RawContent["url"], policy.ToJson(ignoreNull: true));
                    matchedRules.Add(policy);
                    // continue;
                }
                else logger.LogInformation("uri hash {uriHash} did not match rule's {ruleUriHash}", Convert.ToHexString(uriHash), policy.Entity);

                if (policy.FileHash is not null && fileHash is not null && policy.FileHash == Convert.ToBase64String(fileHash)) {
                    logger.LogInformation("{url} matched rule {rule} by file hash", @event.RawContent["url"], policy.ToJson(ignoreNull: true));
                    matchedRules.Add(policy);
                    // continue;
                }
                else logger.LogInformation("file hash {fileHash} did not match rule's {ruleFileHash}", Convert.ToBase64String(fileHash), policy.FileHash);

                //check pixels every 10% of the way through the image using ImageSharp
                // var image = Image.Load(await _hs._httpClient.GetStreamAsync(resolvedUri));
            }
        else logger.LogInformation("No active media file policies");
        // logger.LogInformation("{url} did not match any rules", @event.RawContent["url"]);

        return matchedRules;
    }

    #endregion

    #region Internal code

    #region Summarisation

    private static (string Raw, string Html) SummariseStateTypeCounts(IList<StateEventResponse> states) {
        string raw = "Count | State type | Mapped type", html = "<table><tr><th>Count</th><th>State type</th><th>Mapped type</th></tr>";
        var groupedStates = states.GroupBy(x => x.Type).ToDictionary(x => x.Key, x => x.ToList()).OrderByDescending(x => x.Value.Count);
        foreach (var (type, stateGroup) in groupedStates) {
            raw += $"\n{stateGroup.Count} | {type} | {stateGroup[0].MappedType.Name}";
            html += $"<tr><td>{stateGroup.Count}</td><td>{type}</td><td>{stateGroup[0].MappedType.Name}</td></tr>";
        }

        html += "</table>";
        return (raw, html);
    }

    #endregion

    #endregion

}
