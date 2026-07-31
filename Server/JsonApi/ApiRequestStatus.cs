using Shared;
using Shared.Packet.Packets;
using System.Net;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Server.JsonApi;

using Mutators = Dictionary<string, Action<ApiRequestStatus.Player, Client>>;

public static class ApiRequestStatus {
    public static async Task<bool> Send(Context ctx) {
        StatusResponse resp = new StatusResponse {
            Settings = GetSettings(ctx),
            Players  = Player.GetPlayers(ctx),
        };
        await ctx.Send(resp);
        return true;
    }


    private static JsonNode? GetSettings(Context ctx)
    {
        if (ctx.HasPermission("Status/Settings/*")) {
            JsonNode? fullSettings = JsonSerializer.SerializeToNode(Settings.Instance);
            if (fullSettings is JsonObject settingsObject) {
                settingsObject.Remove("JsonApi");
            }
            return fullSettings;
        }

        // output object
        JsonObject settings = new JsonObject();

        // all permissions for Settings
        var allowedSettings = ctx.Permissions
            .Where(str => str.StartsWith("Status/Settings/"))
            .Where(str => str != "Status/Settings/*")
            .Select(str => str.Substring(16))
        ;

        bool hasResults = false;

        // copy all allowed Settings
        foreach (string allowedSetting in allowedSettings) {
            string lastKey = "";
            JsonNode? next  = settings;
            object input = Settings.Instance;
            JsonObject output = settings;

            // recursively go down the path
            foreach (string key in allowedSetting.Split("/")) {
                lastKey = key;

                if (next == null) { break; }
                output = (JsonObject) next;

                // create the sublayer
                if (!output.ContainsKey(key)) { output.Add(key, new JsonObject()); }

                // traverse down the output object
                output.TryGetPropertyValue(key, out next);

                // traverse down the Settings object
                var prop = input.GetType().GetProperty(key);
                if (prop == null) {
                    JsonApi.Logger.Warn($"Property \"{allowedSetting}\" ({key}) doesn't exist on the Settings object. This is probably a misconfiguration in the settings.json");
                    goto continue2;
                } else {
                    input = prop.GetValue(input, null)!;
                }
            }

            if (lastKey != "") {
                // copy key with the actual value
                output.Remove(lastKey);
                output.Add(lastKey, JsonValue.Create(input));
                hasResults = true;
            }

            continue2:;
        }

        if (!hasResults) { return null; }
        return settings;
    }


    private class StatusResponse {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public JsonNode? Settings { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Player[]? Players { get; set; }
    }


    public class Player {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Guid? Id { get; private set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Name { get; private set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public GameMode? GameMode { get; private set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Kingdom { get; private set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Stage { get; private set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Scenario { get; private set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public PlayerPosition? Position { get; private set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public PlayerRotation? Rotation { get; private set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Tagged { get; private set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public PlayerCostume? Costume { get; private set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Capture { get; private set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Is2D { get; private set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? IPv4 { get; private set; }


        private static readonly Mutators Mutators = new () {
            ["Status/Players/ID"]       = (p, c) => p.Id       = c.Id,
            ["Status/Players/Name"]     = (p, c) => p.Name     = c.Name,
            ["Status/Players/GameMode"] = (p, c) => p.GameMode = GetGameMode(c),
            ["Status/Players/Kingdom"]  = (p, c) => p.Kingdom  = GetKingdom(c),
            ["Status/Players/Stage"]    = (p, c) => p.Stage    = GetGamePacket(c)?.Stage ?? null,
            ["Status/Players/Scenario"] = (p, c) => p.Scenario = GetGamePacket(c)?.ScenarioNum ?? null,
            ["Status/Players/Position"] = (p, c) => p.Position = PlayerPosition.FromVector3(GetPlayerPacket(c)?.Position ?? null),
            ["Status/Players/Rotation"] = (p, c) => p.Rotation = PlayerRotation.FromQuaternion(GetPlayerPacket(c)?.Rotation ?? null),
            ["Status/Players/Tagged"]   = (p, c) => p.Tagged   = GetTagged(c),
            ["Status/Players/Costume"]  = (p, c) => p.Costume  = PlayerCostume.FromClient(c),
            ["Status/Players/Capture"]  = (p, c) => p.Capture  = GetCapture(c),
            ["Status/Players/Is2D"]     = (p, c) => p.Is2D     = GetGamePacket(c)?.Is2d ?? null,
            ["Status/Players/IPv4"]     = (p, c) => p.IPv4     = (c.Socket?.RemoteEndPoint as IPEndPoint)?.Address.ToString(),
        };


        public static Player[]? GetPlayers(Context ctx) {
            if (!ctx.HasPermission("Status/Players"))  { return null; }
            return ctx.Server.ClientsConnected.Select(c => FromClient(ctx, c)).ToArray();
        }


        private static Player FromClient(Context ctx, Client c) {
            Player player = new Player();
            foreach (var (perm, mutate) in Mutators) {
                if (ctx.HasPermission(perm))  {
                    mutate(player, c);
                }
            }
            return player;
        }


        private static GamePacket? GetGamePacket(Client c) {
            c.Metadata.TryGetValue("lastGamePacket", out object? packet);
            if (packet == null) { return null; }
            return (GamePacket) packet;
        }


        private static PlayerPacket? GetPlayerPacket(Client c) {
            c.Metadata.TryGetValue("lastPlayerPacket", out object? packet);
            if (packet == null) { return null; }
            return (PlayerPacket) packet;
        }


        private static GameMode? GetGameMode(Client c) {
            c.Metadata.TryGetValue("gameMode", out object? mode);
            return (GameMode?) mode;
        }


        private static bool? GetTagged(Client c) {
            c.Metadata.TryGetValue("seeking", out object? seeking);
            return (bool?) seeking;
        }


        private static string? GetCapture(Client c) {
            c.Metadata.TryGetValue("lastCapturePacket", out object? packet);
            if (packet == null) { return null; }
            CapturePacket p = (CapturePacket) packet;
            if (p.ModelName == "") { return null; }
            return p.ModelName;
        }


        private static string? GetKingdom(Client c) {
            string? stage = GetGamePacket(c)?.Stage ?? null;
            if (stage == null) { return null; }

            Stages.Stage2Alias.TryGetValue(stage, out string? alias);
            if (alias == null) { return null; }

            if (Stages.Alias2Kingdom.Contains(alias)) {
                return (string?) Stages.Alias2Kingdom[alias];
            }

            return null;
        }
    }


    public class PlayerCostume {
        public string Cap { get; private set; }
        public string Body { get; private set; }


        private PlayerCostume(CostumePacket p) {
            Cap  = p.CapName;
            Body = p.BodyName;
        }


        public static PlayerCostume? FromClient(Client c) {
            if (c.CurrentCostume == null) { return null; }
            CostumePacket p = (CostumePacket) c.CurrentCostume!;
            return new PlayerCostume(p);
        }
    }


    public class PlayerPosition(float x, float y, float z) {
        public float X { get; private set; } = x;
        public float Y { get; private set; } = y;
        public float Z { get; private set; } = z;

        public static PlayerPosition? FromVector3(Vector3? pos) {
            if (pos == null) { return null; }
            Vector3 p = (Vector3) pos;
            return new PlayerPosition(p.X, p.Y, p.Z);
        }
    }


    public class PlayerRotation (float w, float x, float y, float z) {
        public float W { get; private set; } = w;
        public float X { get; private set; } = x;
        public float Y { get; private set; } = y;
        public float Z { get; private set; } = z;

        public static PlayerRotation? FromQuaternion(Quaternion? quat) {
            if (quat == null) { return null; }
            Quaternion q = (Quaternion) quat;
            return new PlayerRotation(q.W, q.X, q.Y, q.Z);
        }
    }
}
