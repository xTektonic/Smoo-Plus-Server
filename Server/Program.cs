using System.Collections.Concurrent;
using System.Net;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Server.JsonApi;
using Server;
using Sever.Server;
using Shared;
using Shared.Packet;
using Shared.Packet.Packets;
using Timer = System.Timers.Timer;

Server.Server server = new Server.Server();
HashSet<int> shineBag = new HashSet<int>();
CancellationTokenSource cts = new CancellationTokenSource();
Logger consoleLogger = new Logger("Console");
DiscordBot bot = new DiscordBot();
await bot.Run();

consoleLogger.Info("Server started!");

async Task PersistShines() {
    if (!Settings.Instance.PersistShines.Enabled) {
        return;
    }

    try {
        string shineJson = JsonSerializer.Serialize(shineBag);
        await File.WriteAllTextAsync(Settings.Instance.PersistShines.Filename, shineJson);
    }
    catch (Exception ex) {
        consoleLogger.Error(ex);
    }
}

async Task LoadShines() {
    if (!Settings.Instance.PersistShines.Enabled) {
        return;
    }
    try {
        string shineJson = await File.ReadAllTextAsync(Settings.Instance.PersistShines.Filename);
        var loadedShines = JsonSerializer.Deserialize<HashSet<int>>(shineJson);

        if (loadedShines is not null) shineBag = loadedShines;
    }
    catch (FileNotFoundException) { }
    catch (Exception ex) {
        consoleLogger.Error(ex);
    }
}

// Load shines table from file
await LoadShines();

server.ClientJoined += (c, _) => {
    c.Metadata["shineBag"] = new ConcurrentBag<int>();
    c.Metadata["loadedSave"] = false;
    c.Metadata["scenario"] = (byte?)0;
    c.Metadata["2d"] = false;
    c.Metadata["disableShineSync"] = false;
};

async Task ClientSyncShineBag(Client client, bool force = false) {
    if (!Settings.Instance.Shines.Enabled) return;
    try {
        if ((bool?)client.Metadata["disableShineSync"] ?? false) return;
        ConcurrentBag<int> clientBag = (ConcurrentBag<int>)(client.Metadata["shineBag"] ??= new ConcurrentBag<int>());
        foreach (int shine in shineBag) {
            if (!force && (clientBag.Contains(shine) || Settings.Instance.Shines.Excluded.Contains(shine))) continue;
            if (!client.Connected) return;
            await client.Send(new ShinePacket { ShineId = shine });
            clientBag.Add(shine);
        }
    }
    catch {
        // errors that can happen when sending will crash the server :)
    }
}

async void SyncShineBag(bool force = false) {
    try {
        await PersistShines();
        await Parallel.ForEachAsync(server.ClientsConnected.ToArray(), async (client, _) => await ClientSyncShineBag(client, force));
    }
    catch {
        // errors that can happen shines change will crash the server :)
    }
}

Timer timer = new Timer(120000) { // 2 minutes
    AutoReset = true,
    Enabled = true,
};
timer.Elapsed += (_, _) => { SyncShineBag(); };

void LogError(Task x) {
    if (x.Exception != null)
    {
        consoleLogger.Error(x.Exception.ToString());
    }
}

server.PacketHandler = (client, packet) => {
    switch (packet)
    {
        //TODO: change clear shine bag to game start packet
        case GamePacket gamePacket: {
            // crash ignored player
            if (client.Ignored) {
                client.Logger.Info($"Crashing ignored player after entering stage {gamePacket.Stage}.");
                BanLists.Crash(client, 500);
                return false;
            }

            // crash player entering a banned stage
            if (BanLists.Enabled && BanLists.IsStageBanned(gamePacket.Stage)) {
                client.Logger.Warn($"Crashing player for entering banned stage {gamePacket.Stage}.");
                BanLists.Crash(client, 500);
                return false;
            }

            client.Logger.Info($"Got game packet {gamePacket.Stage}->{gamePacket.ScenarioNum}");

            // reset lastPlayerPacket on stage changes
            client.Metadata.TryGetValue("lastGamePacket", out object? old);
            if (old != null && ((GamePacket)old).Stage != gamePacket.Stage) {
                client.Metadata["lastPlayerPacket"] = null;
            }

            client.Metadata["scenario"] = gamePacket.ScenarioNum;
            client.Metadata["2d"] = gamePacket.Is2d;
            client.Metadata["lastGamePacket"] = gamePacket;

            switch (gamePacket.Stage)
            {
                case "CapWorldHomeStage" when gamePacket.ScenarioNum == 1:
                case "CapWorldTowerStage" when gamePacket.ScenarioNum == 1:
                    if (!((bool?)client.Metadata["disableShineSync"] ?? false))
                    {
                        client.Metadata["disableShineSync"] = true;
                        ((ConcurrentBag<int>)(client.Metadata["shineBag"] ??= new ConcurrentBag<int>())).Clear();
                        client.Logger.Info("Entered Cap on new save, preventing moon sync until Cascade");
                        if (Settings.Instance.Shines.ClearOnNewSaves)
                        {
                            shineBag.Clear();
                            client.Logger.Info("Cleared shine bags");
                            Task.Run(PersistShines);
                        }
                    }
                    break;
                default:
                    if ((bool?)client.Metadata["disableShineSync"] ?? false)
                    {
                        Task.Run(async () =>
                        {
                            client.Logger.Info("Entered Cascade or later with moon sync disabled, enabling moon sync again");
                            await Task.Delay(2000);
                            client.Metadata["disableShineSync"] = false;
                            await ClientSyncShineBag(client);
                        });
                    }
                    break;
            }

            if (Settings.Instance.Scenario.MergeEnabled)
            {
                server.BroadcastReplace(gamePacket, client, (from, to, gp) =>
                {
                    gp.ScenarioNum = (byte?)to.Metadata["scenario"] ?? 200;
#pragma warning disable CS4014
                    to.Send(gp, from).ContinueWith(LogError);
#pragma warning restore CS4014
                });
                return false;
            }

            break;
        }

        // ignore all other packets from ignored players
        case not null when client.Ignored: {
                return false;
            }

        case TagPacket tagPacket: {
            if (BanLists.Enabled && BanLists.IsGameModeBanned(tagPacket.GameMode))
            {
                client.Logger.Warn($"Crashing player for entering banned gamemode {tagPacket.GameMode}.");
                BanLists.Crash(client, 500);
                return false;
            }
            
            if ( tagPacket is {GameMode: GameMode.Legacy, UpdateType: TagPacket.TagUpdate.Both}
                || tagPacket.GameMode == GameMode.HideAndSeek
                || tagPacket.GameMode == GameMode.Sardines
                || tagPacket.GameMode == GameMode.FreezeTag
            )
            { 
                client.Logger.Info($"Got tag packet: {tagPacket.GameMode} {tagPacket.UpdateType} {tagPacket.IsIt} {tagPacket.Minutes}:{tagPacket.Seconds}");
                if ((tagPacket.UpdateType & TagPacket.TagUpdate.State) != 0)
                {
                    client.Metadata["seeking"] = tagPacket.IsIt;
                }
                if ((tagPacket.UpdateType & TagPacket.TagUpdate.Time) != 0)
                {
                    client.Metadata["time"] = new Time(tagPacket.Minutes, tagPacket.Seconds);
                }
            }
            else
            {
                client.Logger.Info($"Got tag packet: {tagPacket.GameMode} {(byte) tagPacket.UpdateType}");
                client.Metadata["seeking"] = null;
                client.Metadata["time"] = null;
            }
            client.Metadata["gameMode"] = tagPacket.GameMode;
            break;
        }

        case CapturePacket capturePacket: {
            client.Logger.Info($"Got capture packet: {capturePacket.ModelName}");
            client.Metadata["lastCapturePacket"] = capturePacket;
            break;
        }

        case CostumePacket costumePacket: {
            client.Logger.Info($"Got costume packet: {costumePacket.BodyName}, {costumePacket.CapName}");
            client.Metadata["lastCostumePacket"] = costumePacket;
            client.CurrentCostume = costumePacket;
#pragma warning disable CS4014
            ClientSyncShineBag(client); //no point logging since entire def has try/catch
#pragma warning restore CS4014
            client.Metadata["loadedSave"] = true;
            break;
        }

        case ShinePacket shinePacket: {
            if (!Settings.Instance.Shines.Enabled) return false;
            if (Settings.Instance.Shines.Excluded.Contains(shinePacket.ShineId))
            {
                client.Logger.Info($"Got moon {shinePacket.ShineId} (excluded)");
                return false;
            }
            if (client.Metadata["loadedSave"] is false) break;
            
            ConcurrentBag<int> playerBag = (ConcurrentBag<int>)client.Metadata["shineBag"]!;
            shineBag.Add(shinePacket.ShineId);
            if (playerBag.Contains(shinePacket.ShineId)) break;
            client.Logger.Info($"Got moon {shinePacket.ShineId}");
            playerBag.Add(shinePacket.ShineId);
            SyncShineBag();
            break;
        }

        case PlayerPacket playerPacket: {
            client.Metadata["lastPlayerPacket"] = playerPacket;
            break;
        }
        
        case CheckpointPacket checkpointPacket: {
            client.Logger.Info($"Got checkpoint: {Util.CheckpointNames[checkpointPacket.ObjId]}");
            break;
        }
        
        case MoonRockPacket moonRockPacket: {
            client.Logger.Info($"Hit Moon Rock in {Util.KingdomNames[moonRockPacket.WorldId]}");
            break;
        }
        
        case CoinCollectCollPacket coinCollectCollPacket: {
            client.Logger.Info($"Got reginal coin in {Util.KingdomNames[coinCollectCollPacket.WorldId]}");
            break;
        }
        
        case GameStartPacket: {
            client.Logger.Info("Started");
            break;
        }
    }
    return true; // Broadcast packet to all other clients
};

(HashSet<string> failToFind, HashSet<Client> toActUpon, List<(string arg, IEnumerable<string> amb)> ambig) MultiUserCommandHelper(string[] args) {
    HashSet<string> failToFind = new();
    HashSet<Client> toActUpon;
    List<(string arg, IEnumerable<string> amb)> ambig = new();
    if (args[0] == "*")
    {
        toActUpon = new(server.Clients.Where(c => c.Connected));
    }
    else
    {
        toActUpon = args[0] == "!*" ? new(server.Clients.Where(c => c.Connected)) : new();
        for (int i = (args[0] == "!*" ? 1 : 0); i < args.Length; i++) {
            string arg = args[i];
            var search = server.Clients.Where(c => c.Connected && (
                c.Name.ToLower().StartsWith(arg.ToLower())
                || (Guid.TryParse(arg, out Guid res) && res == c.Id)
                || (IPAddress.TryParse(arg, out IPAddress? ip) && ip.Equals(((IPEndPoint)c.Socket!.RemoteEndPoint!).Address))
            )).ToList();
            if (!search.Any()) {
                failToFind.Add(arg); //none found
            }
            else if (search.Count > 1) {
                Client? exact = search.FirstOrDefault(x => x.Name == arg);
                if (!ReferenceEquals(exact, null)) {
                    //even though multiple matches, since exact match, it isn't ambiguous
                    if (args[0] == "!*") {
                        toActUpon.Remove(exact);
                    }
                    else {
                        toActUpon.Add(exact);
                    }
                }
                else {
                    if (ambig.All(x => x.arg != arg)) {
                        ambig.Add((arg, search.Select(x => x.Name))); //more than one match
                    }
                    foreach (var rem in search) { //already a list, no need to copy again
                        toActUpon.Remove(rem);
                    }
                }
            }
            else {
                //only one match, so autocomplete
                if (args[0] == "!*")
                {
                    toActUpon.Remove(search.First());
                }
                else
                {
                    toActUpon.Add(search.First());
                }
            }
        }
    }
    return (failToFind, toActUpon, ambig);
}

#region RegisterComands
CommandHandler.RegisterCommand("rejoin", args => {
    if (args.Length == 0)
    {
        return "Usage: rejoin <* | !* (usernames to not rejoin...) | (usernames to rejoin...)>";
    }

    var res = MultiUserCommandHelper(args);

    StringBuilder sb = new StringBuilder();
    sb.Append(res.toActUpon.Count > 0 ? "Rejoined: " + string.Join(", ", res.toActUpon.Select(x => $"\"{x.Name}\"")) : "");
    sb.Append(res.failToFind.Count > 0 ? "\nFailed to find matches for: " + string.Join(", ", res.failToFind.Select(x => $"\"{x.ToLower()}\"")) : "");
    if (res.ambig.Count > 0)
    {
        res.ambig.ForEach(x =>
        {
            sb.Append($"\nAmbiguous for \"{x.arg}\": {string.Join(", ", x.amb.Select(a => $"\"{a}\""))}");
        });
    }

    foreach (Client user in res.toActUpon)
    {
        user.Dispose();
    }

    return sb.ToString();
});

CommandHandler.RegisterCommand("crash", args => {
    if (args.Length == 0)
    {
        return "Usage: crash <* | !* (usernames to not crash...) | (usernames to crash...)>";
    }

    var res = MultiUserCommandHelper(args);

    StringBuilder sb = new StringBuilder();
    sb.Append(res.toActUpon.Count > 0 ? "Crashed: " + string.Join(", ", res.toActUpon.Select(x => $"\"{x.Name}\"")) : "");
    sb.Append(res.failToFind.Count > 0 ? "\nFailed to find matches for: " + string.Join(", ", res.failToFind.Select(x => $"\"{x.ToLower()}\"")) : "");
    if (res.ambig.Count > 0)
    {
        res.ambig.ForEach(x =>
        {
            sb.Append($"\nAmbiguous for \"{x.arg}\": {string.Join(", ", x.amb.Select(a => $"\"{a}\""))}");
        });
    }

    foreach (Client user in res.toActUpon)
    {
        BanLists.Crash(user);
    }

    return sb.ToString();
});

CommandHandler.RegisterCommand("ban", args => BanLists.HandleBanCommand(args, MultiUserCommandHelper));
CommandHandler.RegisterCommand("unban", args => BanLists.HandleUnbanCommand(args));

CommandHandler.RegisterCommand("send", args => {
    const string optionUsage = "Usage: send <stage> <id> <scenario[-1..127]> <* | !* (players to exclude) | (players to send)>";
    if (args.Length < 4) {
        return optionUsage;
    }

    string? stage = Stages.Input2Stage(args[0]);
    if (stage == null) {
        return "Invalid Stage Name! ```" + Stages.KingdomAliasMapping() + "```";
    }

    string id = args[1];

    if (!sbyte.TryParse(args[2], out sbyte scenario) || scenario < -1)
        return $"Invalid scenario number {args[2]} (range: [-1 to 127])";

    Client[] players = MultiUserCommandHelper(args[3..]).toActUpon.ToArray();
    Parallel.ForEachAsync(players, async (c, _) =>
    {
        await c.Send(new ChangeStagePacket
        {
            Stage = stage,
            Id = id,
            Scenario = scenario,
            SubScenarioType = 0
        });
    }).Wait();
    return $"Sent players to {stage}:{scenario}";
});

CommandHandler.RegisterCommand("sendall", args => {
    const string optionUsage = "Usage: sendall <stage>";
    if (args.Length < 1)
    {
        return optionUsage;
    }

    string? stage = Stages.Input2Stage(args[0]);
    if (stage == null)
    {
        return "Invalid Stage Name! ```" + Stages.KingdomAliasMapping() + "```";
    }

    Client[] players = server.Clients.Where(c => c.Connected).ToArray();

    Parallel.ForEachAsync(players, async (c, _) =>
    {
        await c.Send(new ChangeStagePacket
        {
            Stage = stage,
            Id = "",
            Scenario = -1,
            SubScenarioType = 0
        });
    }).Wait();

    return $"Sent players to {stage}:{-1}";
});

// Unnecessary in SMOO+, kept for compatibility
CommandHandler.RegisterCommand("scenario", args => {
    const string optionUsage = "Valid options: merge [true/false]";
    if (args.Length < 1)
        return optionUsage;
    switch (args[0]) {
        case "merge" when args.Length == 2: {
                if (bool.TryParse(args[1], out bool result)) {
                    Settings.Instance.Scenario.MergeEnabled = result;
                    Settings.SaveSettings();
                    return result ? "Enabled scenario merge" : "Disabled scenario merge";
                }
                return optionUsage;
            }
        case "merge" when args.Length == 1: {
                return $"Scenario merging is {Settings.Instance.Scenario.MergeEnabled}";
            }
        default:
            return optionUsage;
    }
});

// Not in SMOO+, kept for compatibility 
CommandHandler.RegisterCommand("tag", args => {
    const string optionUsage =
        "Valid options:\n\ttime <user/*> <freeze/hns/sardine> <minutes[0-65535]> <seconds[0-59]>\n\tseeking <user/*> <freeze/hns/sardine> <true/false>\n\tstart <freeze/hns/sardine> <time> <seekers>";
    if (args.Length < 3)
        return optionUsage;
    switch (args[0])
    {
        case "time" when args.Length == 5:
            {
                if (args[1] != "*" && server.Clients.All(x => x.Name != args[1])) return $"Cannot find user {args[1]}";
                Client? client = server.Clients.FirstOrDefault(x => x.Name == args[1]);
                if (!ushort.TryParse(args[2], out ushort minutes))
                    return $"Invalid time for minutes {args[2]} (range: 0-65535)";
                if (!byte.TryParse(args[3], out byte seconds) || seconds >= 60)
                    return $"Invalid time for seconds {args[3]} (range: 0-59)";
                TagPacket tagPacket = new TagPacket
                {
                    GameMode = GameMode.Legacy,
                    UpdateType = TagPacket.TagUpdate.Time,
                    Minutes = minutes,
                    Seconds = seconds,
                };
                if (args[1] == "*")
                {
                    Parallel.ForEachAsync(server.Clients, async (c, _) =>
                    {
                        await server.Broadcast(tagPacket, c);
                        await c.Send(tagPacket);
                    });
                }
                else if (client != null)
                {
                    _ = server.Broadcast(tagPacket, client);
                    _ = client.Send(tagPacket);
                }
                return $"Set time for {(args[1] == "*" ? "everyone" : args[1])} to {minutes}:{seconds}";
            }
        case "seeking" when args.Length == 5:
            {
                if (args[1] != "*" && server.Clients.All(x => x.Name != args[1])) return $"Cannot find user {args[1]}";
                Client? client = server.Clients.FirstOrDefault(x => x.Name == args[1]);
                if (!bool.TryParse(args[2], out bool seeking)) return $"Usage: tag seeking {args[1]} <true/false>";
                TagPacket tagPacket = new TagPacket
                {
                    GameMode = GameMode.Legacy,
                    UpdateType = TagPacket.TagUpdate.State,
                    IsIt = seeking,
                };
                if (args[1] == "*")
                {
                    Parallel.ForEachAsync(server.Clients, async (c, _) =>
                    {
                        await server.Broadcast(tagPacket, c);
                        await c.Send(tagPacket);
                    });
                }
                else if (client != null)
                {
                    _ = server.Broadcast(tagPacket, client);
                    _ = client.Send(tagPacket);
                }
                return $"Set {(args[1] == "*" ? "everyone" : args[1])} to {(seeking ? "seeker" : "hider")}";
            }
        case "start" when args.Length == 4:
            {
                if (!byte.TryParse(args[1], out byte time)) return $"Invalid countdown seconds {args[1]} (range: 0-255)";
                string[] seekerNames = args[2..];
                Client[] seekers = server.Clients.Where(c => seekerNames.Contains(c.Name)).ToArray();
                if (seekers.Length != seekerNames.Length)
                    return
                        $"Couldn't find seeker{(seekerNames.Length > 1 ? "s" : "")}: {string.Join(", ", seekerNames.Where(name => server.Clients.All(c => c.Name != name)))}";
                // GameMode bestimmen: Wenn mindestens ein Spieler FreezeTag hat, dann FreezeTag, sonst Legacy
                var mode = server.Clients.Select(c => c.Metadata.GetValueOrDefault("gameMode", null))
                    .FirstOrDefault(gm => gm != null && (GameMode)gm == GameMode.FreezeTag);
                var tagMode = mode != null ? GameMode.FreezeTag : GameMode.Legacy;
                consoleLogger.Info($"[DEBUG] tagMode={tagMode}");
                Task.Run(async () =>
                {
                    int realTime = 1000 * time;
                    await Task.Delay(realTime);
                    await Task.WhenAll(
                        Parallel.ForEachAsync(seekers, async (seeker, _) =>
                        {
                            TagPacket packet = new TagPacket
                            {
                                GameMode = tagMode,
                                UpdateType = TagPacket.TagUpdate.State,
                                IsIt = true,
                            };
                            await server.Broadcast(packet, seeker);
                            await seeker.Send(packet);
                        }),
                        Parallel.ForEachAsync(server.Clients.Except(seekers), async (hider, _) =>
                        {
                            TagPacket packet = new TagPacket
                            {
                                GameMode = tagMode,
                                UpdateType = TagPacket.TagUpdate.State,
                                IsIt = false,
                            };
                            await server.Broadcast(packet, hider);
                            await hider.Send(packet);
                        })
                    );
                    consoleLogger.Info($"Started game with seekers {string.Join(", ", seekerNames)} (Mode: {tagMode})");
                });
                return $"Starting game in {time} seconds with seekers {string.Join(", ", seekerNames)} (Mode: {tagMode})";
            }
        default:
            return optionUsage;
    }
});

CommandHandler.RegisterCommand("maxplayers", args => {
    const string optionUsage = "Valid usage: maxplayers <playercount>";
    
    if (args.Length == 0) return $"Max player count: {Settings.Instance.Server.MaxPlayers}";
    if (args.Length > 1) return optionUsage;
    if (!ushort.TryParse(args[0], out ushort maxPlayers)) return "Not a valid number";
    
    Settings.Instance.Server.MaxPlayers = maxPlayers;
    Settings.SaveSettings();
    
    foreach (Client client in server.Clients)
        client.Dispose(); // reconnect all players
    
    return $"Saved and set max players to {maxPlayers}";
});

CommandHandler.RegisterCommand("list",
    _ => $"List:\n\t {string.Join("\n\t", server.Clients.Where(x => x.Connected).Select(x => $"{x.Name} ({x.Id})"))}");


CommandHandler.RegisterCommand("shine", args => {
    const string optionUsage = "Valid options: list, clear, sync, fsync, send, set, include, exclude";
    if (args.Length < 1)
        return optionUsage;
    switch (args[0]) {
        case "list": {
            if (args.Length != 1) return "Usage: shine list";
            return $"Shines: {string.Join(", ", shineBag)}" + (
                Settings.Instance.Shines.Excluded.Any()
                    ? "\nExcluded Shines: " + string.Join(", ", Settings.Instance.Shines.Excluded)
                    : ""
            );
        }
        
        case "clear": {
            if (args.Length != 1) return "Usage: shine clear";
            shineBag.Clear();
            Task.Run(PersistShines);

            foreach (ConcurrentBag<int> playerBag in server.Clients.Select(serverClient =>
                         (ConcurrentBag<int>)serverClient.Metadata["shineBag"]!)) playerBag.Clear();

            return "Cleared shine bags";
        }
        
        case "sync": {
            if (args.Length != 1) return "Usage: shine sync";
            SyncShineBag();
            return "Synced shine bag automatically";
        }

        case "fsync": {
            if (args.Length != 1) return "Usage: shine fsync";
            SyncShineBag(true);
            return "Synced shine bag forcibly";
        }
        
        case "send": {
            if (args.Length < 2) return "Usage: shine send <id> <* | !* (players to exclude) | (players to include)>";
            if (int.TryParse(args[1], out int id))
            {
                Client[] players = (args.Length == 2)
                    ? server.Clients.Where(c => c.Connected).ToArray()
                    : MultiUserCommandHelper(args[2..]).toActUpon.ToArray();
                Parallel.ForEachAsync(players, async (c, _) =>
                {
                    await c.Send(new ShinePacket
                    {
                        ShineId = id
                    });
                }).Wait();
                return $"Sent Shine Num {id}";
            }
            return "Shine ID invalid";
            
        }
        
        case "set": {
            if (args.Length != 2) {
                return "Usage: shine set <true/false>";
            }
            if (bool.TryParse(args[1], out bool result)) {
                Settings.Instance.Shines.Enabled = result;
                Settings.SaveSettings();
                return result ? "Enabled shine sync" : "Disabled shine sync";
            }

            return optionUsage;
        }
        
        case "exclude":
        case "include": {
            if (args.Length != 2) return $"Usage: shine {args[0]} <id>";
            if (int.TryParse(args[1], out int sid)) {
                if (args[0] == "exclude")
                {
                    Settings.Instance.Shines.Excluded.Add(sid);
                    Settings.SaveSettings();
                    return $"Exclude shine {sid} from syncing.";
                }
                
                Settings.Instance.Shines.Excluded.Remove(sid);
                Settings.SaveSettings();
                return $"No longer exclude shine {sid} from syncing.";
                
            }
            return "Shine ID invalid";
        }
        
        default:
            return optionUsage;
    }
});


CommandHandler.RegisterCommand("loadsettings", _ => {
    Settings.LoadSettings();
    return "Loaded settings.json";
});

CommandHandler.RegisterCommand("restartserver", args =>{
    if (args.Length != 0) {
        return "Usage: restartserver";
    }
    
    consoleLogger.Info("Received restartserver command");
    cts.Cancel();
    return "Restarting...";
    
});
#endregion

Console.CancelKeyPress += (_, e) => {
    e.Cancel = true;
    consoleLogger.Info("Received Ctrl+C");
    cts.Cancel();
};

CommandHandler.RegisterCommandAliases(_ => {
    cts.Cancel();
    return "Shutting down";
}, "exit", "quit", "q");

#pragma warning disable CS4014
Task.Run(() => {
    consoleLogger.Info("Run help command for valid commands.");
    while (true) {
        string? text = Console.ReadLine();
        if (text != null) {
            foreach (string returnString in CommandHandler.GetResult(text).ReturnStrings) {
                consoleLogger.Info(returnString);
            }
        }
    }
}).ContinueWith(LogError);
#pragma warning restore CS4014

#region WebInterface
Task? webTask = null;
if(false)// (Settings.Instance.WebInterface.Enabled)
{
    webTask = Task.Run(async () =>
    {

        cts.Token.ThrowIfCancellationRequested();
        var listener = new HttpListener();
        string address = Settings.Instance.WebInterface.Address ?? "localhost";
        ushort port = Settings.Instance.WebInterface.Port;
        listener.Prefixes.Add($"http://{address}:{port}/");
        listener.Start();
        consoleLogger.Info($"Webinterface running on http://{address}:{port}/dashboard.html");
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"http://{address}:{port}/dashboard.html",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            consoleLogger.Info("Could not open browser: " + ex.Message);
        }

        while (listener.IsListening)
        {
            cts.Token.ThrowIfCancellationRequested();
            var context = await listener.GetContextAsync();
            string urlPath = context.Request.Url!.AbsolutePath.TrimStart('/').ToLower();
            string filePath = Path.Combine(AppContext.BaseDirectory, "web-interface", urlPath.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                // API: Handle JSON API requests
                if (urlPath == "api" && context.Request.HttpMethod == "POST")
                {
                    try
                    {
                        using var reader = new StreamReader(context.Request.InputStream);
                        string body = await reader.ReadToEndAsync();
                        var json = JsonDocument.Parse(body);

                        if (json.RootElement.TryGetProperty("Type", out var typeElement))
                        {
                            string type = typeElement.GetString() ?? "";

                            // Create a context for the API request with the HTTP context
                            var ctx = new Context(server, context);

                            try
                            {
                                // Handle the request based on type
                                switch (type)
                                {
                                    case "Settings":
                                        await ApiRequestSettings.Send(ctx);
                                        break;
                                    case "ConsoleCommand":
                                        if (json.RootElement.TryGetProperty("Command", out var commandElement))
                                        {
                                            string command = commandElement.GetString() ?? "";
                                            if (!string.IsNullOrWhiteSpace(command))
                                            {
                                                consoleLogger.Info($"[Dashboard] Executing command: {command}");
                                                var result = CommandHandler.GetResult(command);
                                                var response = new { success = true, output = result.ReturnStrings };
                                                await ctx.Send(response);
                                            }
                                            else
                                            {
                                                context.Response.StatusCode = 400;
                                                await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("Command cannot be empty"));
                                            }
                                        }
                                        else
                                        {
                                            context.Response.StatusCode = 400;
                                            await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("Missing 'Command' in request"));
                                        }
                                        break;
                                    case "Stages":
                                        await ApiRequestStages.Send(ctx);
                                        break;
                                    default:
                                        context.Response.StatusCode = 400;
                                        await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes($"Unknown API type: {type}"));
                                        break;
                                }
                            }
                            catch (Exception ex)
                            {
                                // Log the error and return a 500 response
                                consoleLogger.Error($"Error handling API request: {ex}");
                                context.Response.StatusCode = 500;
                                var errorResponse = new { error = "Internal server error", details = ex.Message };
                                string errorJson = JsonSerializer.Serialize(errorResponse);
                                await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(errorJson));
                            }
                            finally
                            {
                                if (context.Response.OutputStream.CanWrite)
                                {
                                    context.Response.OutputStream.Close();
                                }
                            }
                            continue;
                        }
                        else
                        {
                            context.Response.StatusCode = 400;
                            await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("Missing 'Type' in request"));
                            context.Response.OutputStream.Close();
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        context.Response.StatusCode = 500;
                        await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes($"Error processing API request: {ex.Message}"));
                        context.Response.OutputStream.Close();
                        consoleLogger.Error($"API error: {ex}");
                        continue;
                    }
                }

                // API: Serverinfo
                if (urlPath.StartsWith("api/serverinfo"))
                {
                    var settings = Settings.Instance.Server;
                    string response = $"{{\"host\": \"{settings.Address}\", \"port\": {settings.Port}, \"maxPlayers\": {settings.MaxPlayers}}}";
                    context.Response.ContentType = "application/json";
                    byte[] buffer = Encoding.UTF8.GetBytes(response);
                    context.Response.ContentLength64 = buffer.Length;
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    context.Response.OutputStream.Close();
                    continue;
                }

                // API: Konsolenbefehl ausführen
                if (urlPath == "commands/exec" && context.Request.HttpMethod == "POST")
                {
                    using var reader = new StreamReader(context.Request.InputStream);
                    string body = reader.ReadToEnd();
    
                    var json = JsonDocument.Parse(body);
                    string command = json.RootElement.GetProperty("command").GetString() ?? "";

                    var result = CommandHandler.GetResult(command);
                    string output = string.Join("\n", result.ReturnStrings);

                    // Kommando und Ausgabe ins Log schreiben
                    consoleLogger.Info($"> {command}\n{output}");

                    context.Response.ContentType = "text/plain";
                    byte[] buffer = Encoding.UTF8.GetBytes(output);
                    context.Response.ContentLength64 = buffer.Length;
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    context.Response.OutputStream.Close();
                    continue;
                }

                // API: Konsolen-Log abrufen
                if (urlPath == "commands/output" && context.Request.HttpMethod == "GET")
                {
                    string output = Logger.GetGlobalOutput();
                    context.Response.ContentType = "text/plain";
                    byte[] buffer = Encoding.UTF8.GetBytes(output);
                    context.Response.ContentLength64 = buffer.Length;
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    context.Response.OutputStream.Close();
                    continue;
                }

                // API: Dummy-Console
                if (urlPath.StartsWith("api/console"))
                {
                    string response = "{\"output\": [\"API not implemented\"]}";
                    context.Response.ContentType = "application/json";
                    byte[] buffer = Encoding.UTF8.GetBytes(response);
                    context.Response.ContentLength64 = buffer.Length;
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    context.Response.OutputStream.Close();
                    continue;
                }

                // API: Banlist
                if (urlPath.StartsWith("api/banlist"))
                {
                    var banPlayers = Settings.Instance.BanList.Players?.Select(guid => guid.ToString()).ToArray() ?? Array.Empty<string>();
                    var banStages = Settings.Instance.BanList.Stages?.ToArray() ?? Array.Empty<string>();
                    string response = JsonSerializer.Serialize(new
                    {
                        players = banPlayers,
                        stages = banStages
                    });
                    context.Response.ContentType = "application/json";
                    byte[] buffer = Encoding.UTF8.GetBytes(response);
                    context.Response.ContentLength64 = buffer.Length;
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    context.Response.OutputStream.Close();
                    continue;
                }
                // API: Playerlist
                if (urlPath.StartsWith("api/players"))
                {
                    var players = server.Clients
                        .Where(c => c.Connected)
                        .Select(c =>
                        {
                            float? posX = null, posY = null;
                            if (c.Metadata.TryGetValue("lastPlayerPacket", out var playerPacketObj))
                            {
                                var pp = (PlayerPacket?)playerPacketObj;
                                posX = pp?.Position.X;
                                posY = pp?.Position.Y;
                            }
                            // Capture-Objekt auslesen
                            string capture = "";
                            if (c.Metadata.TryGetValue("lastCapturePacket", out var capturePacketObj) && capturePacketObj is CapturePacket cp)
                            {
                                capture = cp.ModelName;
                            }
                            // GameMode auslesen
                            string? gameMode = "";
                            if (c.Metadata.TryGetValue("gameMode", out var gmObj) && gmObj != null)
                            {
                                GameMode? gmEnum = null;
                                if (gmObj is GameMode gme)
                                {
                                    gmEnum = gme;
                                }
                                else if (gmObj is int gmi)
                                {
                                    gmEnum = (GameMode)gmi;
                                }
                                else if (gmObj is sbyte gmsb)
                                {
                                    gmEnum = (GameMode)gmsb;
                                }
                                else if (gmObj is string gms && Enum.TryParse<GameMode>(gms, out var parsed))
                                {
                                    gmEnum = parsed;
                                }
                                if (gmEnum != null)
                                {
                                    gameMode = gmEnum.ToString();
                                    if ((gmEnum == GameMode.HideAndSeek || gmEnum == GameMode.Sardines || gmEnum == GameMode.FreezeTag)
                                        && c.Metadata.TryGetValue("seeking", out var seekObj) && seekObj != null)
                                    {
                                        bool isSeeker = false;
                                        if (bool.TryParse(seekObj.ToString(), out bool parsedSeek)) isSeeker = parsedSeek;
                                        if (gmEnum == GameMode.HideAndSeek)
                                        {
                                            gameMode += isSeeker ? " (Seeker)" : " (Hider)";
                                        }
                                        else if (gmEnum ==GameMode.FreezeTag)
                                        {
                                            gameMode += isSeeker ? " (Chaser)" : " (Runner)";
                                        }
                                        else if (gmEnum == GameMode.Sardines)
                                        {
                                            gameMode += isSeeker ? " (Büchse)" : " (Sardine)";
                                        }
                                    }
                                }
                                else
                                {
                                    gameMode = gmObj.ToString();
                                }
                            }
                            // Neue Stats auslesen
                            int lives = c.Metadata.TryGetValue("lives", out var livesObj) ? Convert.ToInt32(livesObj) : 0;
                            int coins = c.Metadata.TryGetValue("coins", out var coinsObj) ? Convert.ToInt32(coinsObj) : 0;
                            string outfit = c.Metadata.TryGetValue("outfit", out var outfitObj) ? outfitObj?.ToString() ?? "" : "";
                            float speed = c.Metadata.TryGetValue("speed", out var speedObj) ? Convert.ToSingle(speedObj) : 1.0f;
                            float jumpHeight = c.Metadata.TryGetValue("jumpHeight", out var jumpHeightObj) ? Convert.ToSingle(jumpHeightObj) : 1.0f;
                            // Cap/Body ggf. Override verwenden
                            string cap = c.Metadata.TryGetValue("capOverride", out var capObj) && capObj is string co && !string.IsNullOrEmpty(co)
                                ? co : (c.CurrentCostume?.CapName ?? "");
                            string body = c.Metadata.TryGetValue("bodyOverride", out var bodyObj) && bodyObj is string bo && !string.IsNullOrEmpty(bo)
                                ? bo : (c.CurrentCostume?.BodyName ?? "");
                            return new
                            {
                                 c.Name,
                                IPv4 = c.Connected ? ((IPEndPoint)c.Socket?.RemoteEndPoint!).Address.ToString() : null,
                                 c.Banned,
                                 c.Ignored,
                                Cap = cap,
                                Body = body,
                                Capture = capture,
                                GameMode = gameMode,
                                Stage = c.Metadata.TryGetValue("lastGamePacket", out var gpObj) ? ((GamePacket?)gpObj)?.Stage : "",
                                PosX = posX,
                                PosY = posY,
                                Lives = lives,
                                Coins = coins,
                                Outfit = outfit,
                                Speed = speed,
                                JumpHeight = jumpHeight
                            };
                        }).ToArray();

                    string response = JsonSerializer.Serialize(new { Players = players });
                    context.Response.ContentType = "application/json";
                    byte[] buffer = Encoding.UTF8.GetBytes(response);
                    context.Response.ContentLength64 = buffer.Length;
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                    context.Response.OutputStream.Close();
                    continue;
                }

                // API: Stages
                if (urlPath.StartsWith("api/stages"))
                {
                    try
                    {
                        // Erstelle die Stage-Daten aus der Stages-Klasse
                        var stagesByKingdom = new Dictionary<string, List<string>>();
                        var stageToKingdom = new Dictionary<string, string>();
                        var kingdomToStage = new Dictionary<string, string>();
                        var mapImages = new Dictionary<string, string>();

                        // Erstelle stagesByKingdom aus Stage2Alias und Alias2Kingdom
                        foreach (var stageEntry in Stages.Stage2Alias)
                        {
                            var stage = stageEntry.Key;
                            var alias = stageEntry.Value;

                            // Verwende ContainsKey und Indexer für OrderedDictionary
                            if (Stages.Alias2Kingdom.Contains(alias))
                            {
                                var kingdom = Stages.Alias2Kingdom[alias]?.ToString();
                                if (!string.IsNullOrEmpty(kingdom))
                                {
                                    if (!stagesByKingdom.TryGetValue(kingdom, out _))
                                    {
                                        stagesByKingdom[kingdom] = new List<string>();
                                    }
                                    stagesByKingdom[kingdom].Add(stage);

                                    // Erstelle stageToKingdom Mapping
                                    stageToKingdom[stage] = kingdom;

                                    // Erstelle kingdomToStage Mapping für Home Stages
                                    if (stage.Contains("HomeStage"))
                                    {
                                        kingdomToStage[kingdom] = stage;
                                    }
                                }
                            }
                        }

                        // Erstelle mapImages basierend auf kingdomToStage
                        foreach (var entry in kingdomToStage)
                        {
                            var kingdom = entry.Key;
                            var homeStage = entry.Value;
                            var kingdomName = kingdom.Replace(" ", "");
                            mapImages[homeStage] = $"{kingdomName}.png";
                        }

                        // Erstelle JSON-Response
                        var response = new
                        {
                            stagesByKingdom,
                            stageToKingdom,
                            kingdomToStage,
                            mapImages
                        };

                        string jsonResponse =JsonSerializer.Serialize(response);
                        context.Response.ContentType = "application/json";
                        byte[] buffer = Encoding.UTF8.GetBytes(jsonResponse);
                        context.Response.ContentLength64 = buffer.Length;
                        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                        context.Response.OutputStream.Close();
                        continue;
                    }
                    catch (Exception ex)
                    {
                        context.Response.StatusCode = 500;
                        byte[] buffer = Encoding.UTF8.GetBytes($"Error loading stages: {ex.Message}");
                        context.Response.ContentLength64 = buffer.Length;
                        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                        context.Response.OutputStream.Close();
                        continue;
                    }
                }

                // Statische Dateien ausliefern
                if (File.Exists(filePath))
                {
                    string ext = Path.GetExtension(filePath).ToLower();
                    context.Response.ContentType = ext switch
                    {
                        ".html" => "text/html",
                        ".css" => "text/css",
                        ".js" => "application/javascript",
                        ".png" => "image/png",
                        ".jpg" => "image/jpeg",
                        ".ico" => "image/x-icon",
                        _ => "application/octet-stream"
                    };
                    byte[] buffer = File.ReadAllBytes(filePath);
                    context.Response.ContentLength64 = buffer.Length;
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                }
                else
                {
                    // Fallback: index.html für SPA-Routing
                    string fallback = Path.Combine(AppContext.BaseDirectory, "web-interface", "index.html");
                    byte[] buffer = File.ReadAllBytes(fallback);
                    context.Response.ContentType = "text/html";
                    context.Response.ContentLength64 = buffer.Length;
                    context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                }
                context.Response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                byte[] buffer = Encoding.UTF8.GetBytes("Internal Server Error\n" + ex);
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();
            }
        }
    }, cts.Token);
}

#endregion

var gameTask = server.Listen(cts.Token);

if (webTask != null)
    await Task.WhenAll(webTask, gameTask);
else
    await gameTask;