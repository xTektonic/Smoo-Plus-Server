namespace Server.JsonApi;

using System.Text.Json.Nodes;

using TypesDictionary = Dictionary<string, Func<Context, Task<bool>>>;

public class ApiRequest {
    public string? Token { get; set; }
    public string? Type { get; set; }
    public JsonNode? Data { get; set; }


    private static readonly TypesDictionary Types = new () {
        ["Status"]      = async ctx => await ApiRequestStatus.Send(ctx),
        ["Command"]     = async ctx => await ApiRequestCommand.Send(ctx),
        ["Permissions"] = async ctx => await ApiRequestPermissions.Send(ctx),
        ["Stages"]      = async ctx => await ApiRequestStages.Send(ctx),
    };


    public string? GetStringData() {
        return Data?.GetValue<string>();
    }


    public async Task<bool> Process(Context ctx) {
        if (Type != null) {
            return await Types[Type](ctx);
        }
        return false;
    }


    public bool IsValid(Context ctx) {
        if (Token == null) {
            JsonApi.Logger.Warn($"Invalid request missing Token from {ctx.Socket?.RemoteEndPoint}.");
            return false;
        }

        if (Type == null) {
            JsonApi.Logger.Warn($"Invalid request missing Type from {ctx.Socket?.RemoteEndPoint}.");
            return false;
        }

        if (!Types.ContainsKey(Type)) {
            JsonApi.Logger.Warn($"Invalid Type \"{Type}\" from {ctx.Socket?.RemoteEndPoint}.");
            return false;
        }

        if (!Settings.Instance.JsonApi.Tokens.ContainsKey(Token)) {
            JsonApi.Logger.Warn($"Invalid Token from {ctx.Socket?.RemoteEndPoint}.");
            return false;
        }

        return true;
    }
}
