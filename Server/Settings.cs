using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Sever.Server;
using Shared;

namespace Server;

public class Settings {
    public static Settings Instance = new Settings();
    private static readonly Logger Logger = new Logger("Settings");
    public static Action? LoadHandler;

    static Settings() {
        LoadSettings();
    }

    public static void LoadSettings() {
        bool needSave = false;
        if (File.Exists("settings.json")) {
            string text = File.ReadAllText("settings.json");
            try {
                Instance = JsonConvert.DeserializeObject<Settings>(text, new StringEnumConverter(new CamelCaseNamingStrategy())) ?? Instance;
                Logger.Info("Loaded settings from settings.json");
            }
            catch (Exception e) {
                Logger.Warn($"Failed to load settings.json: {e}");
                needSave = true; // Fehler beim Laden: Defaults speichern
            }
        } else {
            needSave = true; // Datei existiert nicht: Defaults speichern
        }
        if (needSave) SaveSettings();
        LoadHandler?.Invoke();
    }

    public static void SaveSettings(bool silent = false) {
        try {
            File.WriteAllText("settings.json", JsonConvert.SerializeObject(Instance, Formatting.Indented, new StringEnumConverter(new CamelCaseNamingStrategy())));
            if (!silent) { Logger.Info("Saved settings to settings.json"); }
        }
        catch (Exception e) {
            Logger.Error($"Failed to save settings.json {e}");
        }
    }

    public readonly ServerTable Server = new();
    public readonly FlipTable Flip = new();
    public readonly ScenarioTable Scenario = new();
    public readonly BanListTable BanList = new();
    public readonly DiscordTable Discord = new();
    public readonly ShineTable Shines = new();
    public readonly PersistShinesTable PersistShines = new();
    public readonly JsonApiTable JsonApi = new();
    public readonly WebInterfaceTable WebInterface = new();

    public class ServerTable {
        public string Address { get; set; } = IPAddress.Any.ToString();
        public ushort Port { get; set; } = 1027;
        public ushort MaxPlayers { get; set; } = 8;
    }

    public class ScenarioTable {
        public bool MergeEnabled { get; set; }
    }

    public class BanListTable {
        public bool Enabled { get; set; }
        public ISet<Guid> Players { get; set; } = new SortedSet<Guid>();
        public ISet<string> IpAddresses { get; set; } = new SortedSet<string>();
        public ISet<string> Stages { get; set; } = new SortedSet<string>();
        public ISet<sbyte> GameModes { get; set; } = new SortedSet<sbyte>();
    }

    public class FlipTable {
        public bool Enabled { get; set; } = true;
        public ISet<Guid> Players { get; set; } = new SortedSet<Guid>();
        public FlipOptions Pov { get; set; } = FlipOptions.Both;
    }

    public class DiscordTable
    {
        public readonly string? Token = null;
        public readonly string Prefix = "$";
        public readonly string? CommandChannel = null;
        public readonly string? LogChannel = null;
    }

    public class ShineTable {
        public bool Enabled = true;
        public readonly ISet<int> Excluded = new SortedSet<int>();
        public readonly bool ClearOnNewSaves = false;
    }

    public class PersistShinesTable
    {
        public readonly bool Enabled = false;
        public readonly string Filename = "./moons.json";
    }

    public class JsonApiTable
    {
        public readonly bool Enabled = false;
        public readonly Dictionary<string, SortedSet<string>> Tokens = new();
    }
    public class WebInterfaceTable
    {
        public readonly string Username = "admin";
        public readonly string Password = "admin";
        public readonly bool Enabled = true;
        public readonly string? Address = "localhost";
        public readonly ushort Port = 8080;
    }
}
