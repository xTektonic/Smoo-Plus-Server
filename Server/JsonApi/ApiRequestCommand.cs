namespace Server.JsonApi;

public static class ApiRequestCommand {
    public static async Task<bool> Send(Context ctx) {
        if (!ctx.HasPermission("Commands")) {
            await Response.Send(ctx, "Error: Missing Commands permission.");
            return true;
        }

        if (!IsValid(ctx)) {
            return false;
        }

        string input = ctx.Request!.GetStringData()!;
        string command = input.Split(" ")[0];

        // help doesn't need permissions and is invidualized to the token
        if (command == "help") {
            List<string> commands = ["help"];
            if (ctx.Permissions.Contains("Commands/*"))
            {
                commands.AddRange(CommandHandler.Handlers.Keys);
            } else {
                commands.AddRange(
                    ctx.Permissions
                        .Where(str => str.StartsWith("Commands/"))
                        .Select(str => str.Substring(9))
                        .Where(cmd => CommandHandler.Handlers.ContainsKey(cmd))
                );
            }
            string commandsStr = string.Join(", ", commands);

            await Response.Send(ctx, $"Valid commands: {commandsStr}");
            return true;
        }

        // no permissions
        if (!ctx.HasPermission($"Commands/{command}") && !ctx.HasPermission("Commands/*")) {
            await Response.Send(ctx, $"Error: Missing Commands/{command} permission.");
            return true;
        }

        // execute command
        JsonApi.Logger.Info($"[Commands] " + input);
        await Response.Send(ctx, CommandHandler.GetResult(input));
        return true;
    }


    private static bool IsValid(Context ctx) {
        string? command = ctx.Request!.GetStringData();

        if (command == null) {
            JsonApi.Logger.Warn($"[Commands] Invalid request. Data is not a \"System.String\" from {ctx.Socket?.RemoteEndPoint}.");
            return false;
        }

        return true;
    }


    private class Response
    {
        public string[]? Output { get; set; }


        public static async Task Send(Context ctx, CommandHandler.Response response)
        {
            Response resp = new() { Output = response.ReturnStrings };
            await ctx.Send(resp);
        }
    }
}
