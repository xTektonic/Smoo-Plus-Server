using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Shared;

namespace Server;

public class DiscordBot {
    private DiscordClient? _discordClient;
    private string? _token;
    private Settings.DiscordTable Config => Settings.Instance.Discord;
    private string Prefix => Config.Prefix;
    private readonly Logger _logger = new Logger("Discord");
    private DiscordChannel? _commandChannel;
    private DiscordChannel? _logChannel;
    private bool _reconnecting;

    public DiscordBot() {
        _token = Config.Token;
        Logger.AddLogHandler(Log);
        CommandHandler.RegisterCommand("dscrestart", _ => {
            // this should be async'ed but i'm lazy
            _reconnecting = true;
            Task.Run(Reconnect);
            return "Restarting Discord bot";
        });
        if (Config.Token == null) return;
        if (Config.CommandChannel == null)
            _logger.Warn("You probably should set your CommandChannel in settings.json");
        if (Config.LogChannel == null)
            _logger.Warn("You probably should set your LogChannel in settings.json");
        Settings.LoadHandler += SettingsLoadHandler;
    }

    private async Task Reconnect() {
        if (_discordClient != null) // usually null prop works, not here though...`
            await _discordClient.DisconnectAsync();
        await Run();
    }

    private async void SettingsLoadHandler() {
        if (_discordClient == null || _token != Config.Token) {
            await Run();
        }

        if (_discordClient == null) {
            _logger.Error(new NullReferenceException("Discord client not setup yet!"));
            return;
        }

        if (Config.CommandChannel != null) {
            try {
                _commandChannel = await _discordClient.GetChannelAsync(ulong.Parse(Config.CommandChannel));
            } catch (Exception e) {
                _logger.Error($"Failed to get command channel \"{Config.CommandChannel}\"");
                _logger.Error(e);
            }
        }

        if (Config.LogChannel != null) {
            try {
                _logChannel = await _discordClient.GetChannelAsync(ulong.Parse(Config.LogChannel));
            } catch (Exception e) {
                _logger.Error($"Failed to get log channel \"{Config.LogChannel}\"");
                _logger.Error(e);
            }
        }
    }

    private static List<string> SplitMessage(string message, int maxSizePerElem = 2000)
    {
        List<string> result = new List<string>();
        for (int i = 0; i < message.Length; i += maxSizePerElem) 
        {
            result.Add(message.Substring(i, message.Length - i < maxSizePerElem ? message.Length - i : maxSizePerElem));
        }
        return result;
    }

    private async void Log(string source, string level, string text, ConsoleColor _) {
        try {
            if (_discordClient != null && _logChannel != null) {
                foreach (string mesg in SplitMessage(Logger.PrefixNewLines(text, $"{level} [{source}]"), 1994)) //room for 6 '`'
                    await _discordClient.SendMessageAsync(_logChannel, $"```{mesg}```");
            }
        } catch (Exception e) {
            // don't log again, it'll just stack overflow the server!
            if (_reconnecting) return; // skip if reconnecting
            await Console.Error.WriteLineAsync("Exception in discord logger");
            await Console.Error.WriteLineAsync(e.ToString());
        }
    }

    public async Task Run() {
        _token = Config.Token;
        _discordClient?.Dispose();
        if (Config.Token == null) {
            _discordClient = null;
            return;
        }

        try {
            _discordClient = new DiscordClient(new DiscordConfiguration {
                Token = Config.Token,
                MinimumLogLevel = LogLevel.None
            });
            await _discordClient.ConnectAsync(new DiscordActivity("Hide and Seek", ActivityType.Competing));
            SettingsLoadHandler();
            _logger.Info(
                $"Discord bot logged in as {_discordClient.CurrentUser.Username}#{_discordClient.CurrentUser.Discriminator}");
            _reconnecting = false;
            string mentionPrefix = $"{_discordClient.CurrentUser.Mention}";
            _discordClient.MessageCreated += async (_, args) => {
                if (args.Author.IsCurrent) return; //dont respond to commands from ourselves (prevent "sql-injection" esq attacks)
                //prevent commands via dm and non-public channels
                if (_commandChannel == null) {
                    if (args.Channel is DiscordDmChannel)
                        return; //no dm'ing the bot allowed!
                }
                else if (args.Channel.Id != _commandChannel.Id && (_logChannel != null && args.Channel.Id != _logChannel.Id))
                    return;
                //run command
                try {
                    DiscordMessage msg = args.Message;
                    string? resp = null;
                    if (string.IsNullOrEmpty(Prefix)) {
                        await msg.Channel.TriggerTypingAsync();
                        resp = string.Join('\n', CommandHandler.GetResult(msg.Content).ReturnStrings);
                    } else if (msg.Content.StartsWith(Prefix)) {
                        await msg.Channel.TriggerTypingAsync();
                        resp = string.Join('\n', CommandHandler.GetResult(msg.Content[Prefix.Length..]).ReturnStrings);
                    } else if (msg.Content.StartsWith(mentionPrefix)) {
                        await msg.Channel.TriggerTypingAsync();
                        resp = string.Join('\n', CommandHandler.GetResult(msg.Content[mentionPrefix.Length..].TrimStart()).ReturnStrings);
                    }
                    if (resp != null)
                    {
                        foreach (string mesg in SplitMessage(resp))
                            await msg.RespondAsync(mesg);
                    }
                } catch (Exception e) {
                    _logger.Error(e);
                }
            };
            _discordClient.ClientErrored += (_, args) => {
                _logger.Error("Discord client caught an error in handler!");
                _logger.Error(args.Exception);
                return Task.CompletedTask;
            };
            _discordClient.SocketErrored += (_, args) => {
                _logger.Error("Discord client caught an error on socket!");
                _logger.Error(args.Exception);
                return Task.CompletedTask;
            };
        } catch (Exception e) {
            _logger.Error("Exception occurred in discord runner!");
            _logger.Error(e);
        }
    }
}
