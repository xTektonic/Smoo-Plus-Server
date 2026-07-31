using System.Text;

namespace Server;

public static class CommandHandler
{
    public delegate Response Handler(string[] args);

    public static Dictionary<string, Handler> Handlers = new ();
    public static Dictionary<string, Handler> HiddenHandlers = new ();
    public static Dictionary<string, Handler> MultiWordHandlers = new ();
    public static Dictionary<string, Handler> MultiWordHiddenHandlers = new ();

    static CommandHandler()
    {
        RegisterCommand("help", _ =>
        {
            var sb = new StringBuilder();
            sb.AppendLine("Available commands:");

            foreach (var cmd in Handlers.Keys)
            {
                sb.Append($"{cmd}, ");
            }
            return sb.ToString()[..(sb.Length - 2)];
        });
    }

    public static void RegisterCommand(string name, Handler handler)
    {
        Handlers[name] = handler;
    }

    public static void UnregisterCommand(string name)
    {
        Handlers.Remove(name);
    }

    public static void RegisterHiddenCommand(string name, Handler handler)
    {
        HiddenHandlers[name] = handler;
    }

    public static void RegisterMultiWordCommand(string name, Handler handler)
    {
        MultiWordHandlers[name] = handler;
    }

    public static void RegisterMultiWordHiddenCommand(string name, Handler handler)
    {
        MultiWordHiddenHandlers[name] = handler;
    }

    public static void RegisterCommandAliases(Handler handler, params string[] names)
    {
        foreach (string name in names)
        {
            Handlers.Add(name, handler);
        }
    }



    /// <summary>
    /// Modified by <b>TheUbMunster</b>
    /// </summary>
    public static Response GetResult(string input)
    {
        try
        {
            string[] args = input.Split(' ');
            if (args.Length == 0) return "No command entered, see help command for valid commands";
            //this part is to allow single arguments that contain spaces (since the game seems to be able to handle usernames with spaces, we need to as well)
            List<string> newArgs = [args[0]];
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i].Length == 0) continue; //empty string (>1 whitespace between arguments).
                
                if (args[i][0] == '\"')
                {
                    //concatenate args until a string ends with a quote
                    StringBuilder sb = new StringBuilder();
                    i--; //fix off-by-one issue
                    do
                    {
                        i++;
                        sb.Append(args[i] + " "); //add space back removed by the string.Split(' ')
                        if (i >= args.Length)
                        {
                            return "Unmatching quotes, make sure that whenever quotes are used, another quote is present to close it (no action was performed).";
                        }
                    } while (args[i][^1] != '\"');
                    newArgs.Add(sb.ToString(1, sb.Length - 3)); //remove quotes and extra space at the end.
                }
                else
                {
                    newArgs.Add(args[i]);
                }
            }
            args = newArgs.ToArray();
            string commandName = args[0];
            // Check for multi-word commands first
            string fullCommand = string.Join(" ", args);
            foreach (var multiWordHandler in MultiWordHandlers)
            {
                if (fullCommand.StartsWith(multiWordHandler.Key + " "))
                {
                    string[] remainingArgs = fullCommand.Substring(multiWordHandler.Key.Length + 1).Split(' ');
                    return multiWordHandler.Value(remainingArgs);
                }
            }
            foreach (var multiWordHiddenHandler in MultiWordHiddenHandlers)
            {
                if (fullCommand.StartsWith(multiWordHiddenHandler.Key + " "))
                {
                    string[] remainingArgs = fullCommand.Substring(multiWordHiddenHandler.Key.Length + 1).Split(' ');
                    return multiWordHiddenHandler.Value(remainingArgs);
                }
            }

            // Then check for single-word commands
            if (Handlers.TryGetValue(commandName, out Handler? handler))
            {
                return handler(args[1..]);
            }
            if (HiddenHandlers.TryGetValue(commandName, out Handler? hiddenHandler))
            {
                return hiddenHandler(args[1..]);
            }

            return $"Invalid command {args[0]}, see help command for valid commands";
        }
        catch (Exception e)
        {
            return $"An error occured while trying to process your command: {e}";
        }
    }

    public class Response
    {
        public string[] ReturnStrings = null!;
        private Response() { }

        public static implicit operator Response(string value) => new Response
        {
            ReturnStrings = value.Split('\n')
        };
        public static implicit operator Response(string[] values) => new Response
        {
            ReturnStrings = values
        };
    }
}