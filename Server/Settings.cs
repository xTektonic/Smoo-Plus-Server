using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Shared;

namespace Server;

public class Settings {
    public static Settings Instance = new ();
    private static readonly Logger Logger = new ("Settings");
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
                needSave = true;
            }
        } else {
            needSave = true;
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

    public ServerTable Server { get; set; } = new();
    public BanListTable BanList { get; set; } = new();
    public DiscordTable Discord { get; set; } = new();
    public ShineTable Shines { get; set; } = new();
    public JsonApiTable JsonApi { get; set; } = new();
    public WebInterfaceTable WebInterface { get; set; } = new();

    public class ServerTable {
        public string Address { get; set; } = IPAddress.Any.ToString();
        public ushort Port { get; set; } = 1027;
        public ushort MaxPlayers { get; set; } = 8;
    }

    public class BanListTable {
        public bool Enabled { get; set; }
        public ISet<Guid> Players { get; set; } = new SortedSet<Guid>();
        public ISet<string> IpAddresses { get; set; } = new SortedSet<string>();
        public ISet<string> Stages { get; set; } = new SortedSet<string>();
        public ISet<sbyte> GameModes { get; set; } = new SortedSet<sbyte>();
    }

    public class DiscordTable
    {
        public string? Token { get; set; } = null;
        public string Prefix { get; set; } = "$";
        public string? CommandChannel { get; set; } = null;
        public string? LogChannel { get; set; } = null;
    }

    public class ShineTable {
        public bool Enabled { get; set; } = true;
        public ISet<int> Excluded { get; set; } = new SortedSet<int>();
        public bool ClearOnNewSaves { get; set; } = false;
        
        public class PersistShinesTable
        {
            public bool Enabled { get; set; } = false;
            public string Filename { get; set; } = "./moons.json";
        }
        
        public PersistShinesTable PersistShines { get; set; } = new();
    }
    
    public class JsonApiTable
    {
        public bool Enabled { get; set; } = false;
        public Dictionary<string, SortedSet<string>> Tokens { get; set; } = new();
    }
    public class WebInterfaceTable
    {
        public string Username { get; set; } = "admin";
        public string Password { get; set; } = "admin";
        public bool Enabled { get; set; } = true;
        public string? Address { get; set; } = "localhost";
        public ushort Port { get; set; } = 8080;
    }
}
