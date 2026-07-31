using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Server;
using Sever.Server;
using Shared;
using Shared.Packet;
using Shared.Packet.Packets;
using Timer = System.Timers.Timer;

Server.Server server = new Server.Server();
HashSet<int> shineBag = new HashSet<int>();
HashSet<CoinCollect> ccBag = new HashSet<CoinCollect>();
HashSet<string> cpBag = new HashSet<string>();
HashSet<int> mrBag = new HashSet<int>();
CancellationTokenSource cts = new CancellationTokenSource();
Logger consoleLogger = new Logger("Console");
DiscordBot bot = new DiscordBot();
bool shouldRun = true;
await bot.Run();

async Task PersistShines() {
    if (!Settings.Syncing.Shines.PersistShines.Enabled) {
        return;
    }

    try {
        string shineJson = JsonConvert.SerializeObject(shineBag);
        await File.WriteAllTextAsync(Settings.Syncing.Shines.PersistShines.Filename, shineJson);
    }
    catch (Exception ex) {
        consoleLogger.Error(ex);
    }
}

async Task LoadShines() {
    if (!Settings.Syncing.Shines.PersistShines.Enabled) {
        return;
    }
    try {
        string shineJson = await File.ReadAllTextAsync(Settings.Syncing.Shines.PersistShines.Filename);
        var loadedShines = JsonConvert.DeserializeObject<HashSet<int>>(shineJson);

        if (loadedShines is not null) shineBag = loadedShines;
    }
    catch (FileNotFoundException) { }
    catch (Exception ex) {
        consoleLogger.Error(ex);
    }
}



server.ClientJoined += (c, _) => {
    c.Metadata["shineBag"] = new ConcurrentBag<int>();
    c.Metadata["ccBag"] = new ConcurrentBag<CoinCollect>();
    c.Metadata["cpBag"] = new ConcurrentBag<string>();
    c.Metadata["mrBag"] = new ConcurrentBag<int>();
    c.Metadata["scenario"] = (byte?)0;
    c.Metadata["2d"] = false;
};

async Task ClientSyncShineBag(Client client, bool force = false) {
    if (!Settings.Syncing.Shines.Enabled) return;
    try {
        ConcurrentBag<int> clientBag = (ConcurrentBag<int>)(client.Metadata["shineBag"] ??= new ConcurrentBag<int>());
        foreach (int shine in shineBag) {
            if (!force && clientBag.Contains(shine)) continue;
            if (!client.Connected) return;
            await client.Send(new ShinePacket { ShineId = shine });
            clientBag.Add(shine);
        }
    }
    catch {
        // errors that can happen when sending will crash the server :)
    }
}

async Task ClientSyncCcBag(Client client, bool force = false)
{
    if (!Settings.Syncing.Regionals.Enabled) return;
    try {
        ConcurrentBag<CoinCollect> clientBag = (ConcurrentBag<CoinCollect>)(client.Metadata["ccBag"] ??= new ConcurrentBag<CoinCollect>());
        foreach (CoinCollect cc in ccBag) {
            if (!force && clientBag.Contains(cc)) continue;
            if (!client.Connected) return;
            await client.Send(new CoinCollectPacket {
                PlaceId = cc.PlaceId,
                Stage = cc.Stage,
                WorldId = cc.WorldId
            });
            clientBag.Add(cc);
        }
    }
    catch {
        // errors that can happen when sending will crash the server :)
    }
}

async Task ClientSyncCpBag(Client client, bool force = false) {
    if (!Settings.Syncing.Checkpoints.Enabled) return;
    try
    {
        ConcurrentBag<string> clientBag = (ConcurrentBag<string>)(client.Metadata["cpBag"] ??= new ConcurrentBag<string>());
        foreach (string cp in cpBag)
        {
            if (!force && clientBag.Contains(cp)) continue;
            if (!client.Connected) return;
            await client.Send(new CheckpointPacket {
                ObjId = cp
            });
            clientBag.Add(cp);
        }
    }
    catch
    {
        // errors that can happen when sending will crash the server :)
    }
}

async Task ClientSyncMrBag(Client client, bool force = false)
{
    if (!Settings.Syncing.MoonRocks.Enabled) return;
    try
    {
        ConcurrentBag<int> clientBag = (ConcurrentBag<int>)(client.Metadata["mrBag"] ??= new ConcurrentBag<int>());
        foreach (int mr in mrBag)
        {
            if (!force && clientBag.Contains(mr)) continue;
            if (!client.Connected) return;
            await client.Send(new MoonRockPacket { WorldId = mr });
            clientBag.Add(mr);
        }
    }
    catch
    {
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

async void SyncCcBag(bool force = false)
{
    try
    {
        await Parallel.ForEachAsync(server.ClientsConnected.ToArray(), async (client, _) => await ClientSyncCcBag(client, force));
    }
    catch
    {
        // errors that can happen when sending will crash the server :)
    }
}

async void SyncCpBag(bool force = false) {
    try
    {
        await Parallel.ForEachAsync(server.ClientsConnected.ToArray(), async (client, _) => await ClientSyncCpBag(client, force));
    }
    catch
    {
        // errors that can happen when sending will crash the server :)
    }
}

async void SyncMrBag(bool force = false) {
    try
    {
        await Parallel.ForEachAsync(server.ClientsConnected.ToArray(), async (client, _) => await ClientSyncMrBag(client, force));
    }
    catch
    {
        // errors that can happen when sending will crash the server :)
    }
}

Timer timer = new Timer(120000) { // 2 minutes
    AutoReset = true,
    Enabled = true,
};
timer.Elapsed += (_, _) => { SyncShineBag(); };
timer.Elapsed += (_, _) => { SyncCcBag(); };
timer.Elapsed += (_, _) => { SyncCpBag(); };
timer.Elapsed += (_, _) => { SyncMrBag(); };

void LogError(Task x) {
    if (x.Exception != null)
    {
        consoleLogger.Error(x.Exception.ToString());
    }
}

server.PacketHandler = (client, packet) => {
    switch (packet)
    {
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
            break;
        }

        case ShinePacket shinePacket: {
            if (!Settings.Syncing.Shines.Enabled) return false;
            
            ConcurrentBag<int> playerBag = (ConcurrentBag<int>)client.Metadata["shineBag"]!;
            shineBag.Add(shinePacket.ShineId);
            if (playerBag.Contains(shinePacket.ShineId)) break;
            client.Logger.Info($"Got moon {shinePacket.ShineId}");
            playerBag.Add(shinePacket.ShineId);
            
            Parallel.ForEach(server.ClientsConnected.ToArray(),  (c, _) =>
                ((ConcurrentBag<int>)c.Metadata["shineBag"]!).Add(shinePacket.ShineId));
            SyncShineBag();
            break;
        }

        case PlayerPacket playerPacket: {
            client.Metadata["lastPlayerPacket"] = playerPacket;
            break;
        }
        
        case CheckpointPacket checkpointPacket: {
            if(!Settings.Syncing.Checkpoints.Enabled) return false;
            ConcurrentBag<string> playerBag = (ConcurrentBag<string>)client.Metadata["cpBag"]!;
            cpBag.Add(checkpointPacket.ObjId);
            if (playerBag.Contains(checkpointPacket.ObjId)) break;
            client.Logger.Info($"Got checkpoint: {Util.CheckpointNames[checkpointPacket.ObjId]}");
            playerBag.Add(checkpointPacket.ObjId);
            Parallel.ForEach(server.ClientsConnected.ToArray(),  (c, _) =>
                ((ConcurrentBag<string>)c.Metadata["cpBag"]!).Add(checkpointPacket.ObjId));
            SyncCpBag();
            break;
        }
        
        case MoonRockPacket moonRockPacket: {
            if (!Settings.Syncing.MoonRocks.Enabled) return false;
            ConcurrentBag<int> playerBag = (ConcurrentBag<int>)client.Metadata["mrBag"]!;
            mrBag.Add(moonRockPacket.WorldId);
            if (playerBag.Contains(moonRockPacket.WorldId)) break;
            client.Logger.Info($"Hit Moon Rock in {Util.KingdomNames[moonRockPacket.WorldId]}");
            playerBag.Add(moonRockPacket.WorldId);
            Parallel.ForEach(server.ClientsConnected.ToArray(),  (c, _) =>
                ((ConcurrentBag<int>)c.Metadata["mrBag"]!).Add(moonRockPacket.WorldId));
            SyncMrBag();
            break;
        }
        
        case CoinCollectPacket coinCollectCollPacket: {
            if (!Settings.Syncing.Regionals.Enabled) return false;
            ConcurrentBag<CoinCollect> playerBag = (ConcurrentBag<CoinCollect>)client.Metadata["ccBag"]!;
            CoinCollect cc = new(coinCollectCollPacket.PlaceId, coinCollectCollPacket.Stage, coinCollectCollPacket.WorldId);
            ccBag.Add(cc);
            if(playerBag.Contains(cc)) break;
            client.Logger.Info($"Got reginal coin in {Util.KingdomNames[coinCollectCollPacket.WorldId]}");
            playerBag.Add(cc);
            Parallel.ForEach(server.ClientsConnected.ToArray(),  (c, _) =>
                ((ConcurrentBag<CoinCollect>)c.Metadata["ccBag"]!).Add(cc));
            SyncCcBag();
            break;
        }
        
        case GameStartPacket: {
            
            ((ConcurrentBag<int>)(client.Metadata["shineBag"] ??= new ConcurrentBag<int>())).Clear();
            ((ConcurrentBag<CoinCollect>)(client.Metadata["ccBag"] ??= new ConcurrentBag<CoinCollect>()))
                .Clear();
            ((ConcurrentBag<string>)(client.Metadata["cpBag"] ??= new ConcurrentBag<string>())).Clear();
            ((ConcurrentBag<int>)(client.Metadata["mrBag"] ??= new ConcurrentBag<int>())).Clear();
            
            if (Settings.Syncing.Shines.ClearOnNewSaves) {
                shineBag.Clear();
                Task.Run(PersistShines);
                server.Logger.Info("Cleared Shine Bag");
            }
            if (Settings.Syncing.Regionals.CleanOnNewSaves) {
                ccBag.Clear();
                server.Logger.Info("Cleared Region Coin Bag");
            }
            if (Settings.Syncing.Checkpoints.CleanOnNewSaves) {
                cpBag.Clear();
                server.Logger.Info("Cleared Checkpoint Bag");
            }
            if (Settings.Syncing.MoonRocks.CleanOnNewSaves) {
                mrBag.Clear();
                server.Logger.Info("Cleared Moon Rock Bag");
            }
            
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
    
    if (args.Length == 0) return $"Max player count: {Settings.Server.MaxPlayers}";
    if (args.Length > 1) return optionUsage;
    if (!ushort.TryParse(args[0], out ushort maxPlayers)) return "Not a valid number";
    
    Settings.Server.MaxPlayers = maxPlayers;
    Settings.SaveSettings();
    
    foreach (Client client in server.Clients)
        client.Dispose(); // reconnect all players
    
    return $"Saved and set max players to {maxPlayers}";
});

CommandHandler.RegisterCommand("list",
    _ => $"List:\n\t {string.Join("\n\t", server.Clients.Where(x => x.Connected).Select(x => $"{x.Name} ({x.Id})"))}");


CommandHandler.RegisterCommand("shine", args => {
    const string optionUsage = "Valid options: list, clear, sync, fsync, send, set";
    if (args.Length < 1)
        return optionUsage;
    switch (args[0]) {
        case "list": {
            if (args.Length != 1) return "Usage: shine list";
            return $"Shines: {string.Join(", ", shineBag)}";
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
                Settings.Syncing.Shines.Enabled = result;
                Settings.SaveSettings();
                return result ? "Enabled shine sync" : "Disabled shine sync";
            }

            return optionUsage;
        }
        
        default:
            return optionUsage;
    }
});


CommandHandler.RegisterCommand("syncing", args => {
    const string optionUsage = "Valid options: sync, fsync";
    if (args.Length != 1) return optionUsage;
    if (args[0] == "sync") {
        SyncShineBag();
        SyncCcBag();
        SyncCpBag();
        SyncMrBag();
        return "Synced everything";
    }
    if (args[0] == "fsync") {
        SyncShineBag(true);
        SyncCcBag(true);
        SyncCpBag(true);
        SyncMrBag(true);
        return "Synced everything forcibly";
    }
    return optionUsage;
});

CommandHandler.RegisterCommand("loadsettings", _ => {
    Settings.LoadSettings();
    return "Loaded settings.json";
});

CommandHandler.RegisterCommandAliases( args =>{
    shouldRun = true;
    consoleLogger.Info("Received restart command");
    cts.Cancel();
    return "Restarting...";
    
},"restartserver","restart");
#endregion

Console.CancelKeyPress += (_, e) => {
    e.Cancel = true;
    shouldRun = false;
    consoleLogger.Info("Received Ctrl+C");
    cts.Cancel();
};

CommandHandler.RegisterCommandAliases(_ =>
{
    shouldRun = false;
    cts.Cancel();
    return "Shutting down";
}, "exit", "quit", "q");

#pragma warning disable CS4014
Task.Run(() => {
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

while (shouldRun)
{
    cts.Dispose();
    cts = new CancellationTokenSource();
    Settings.LoadSettings();
    // Load shines table from file
    await LoadShines();
    consoleLogger.Info("Server started!");
    consoleLogger.Info("Run help command for valid commands.");
    var gameTask = server.Listen(cts.Token);
    await gameTask;
}