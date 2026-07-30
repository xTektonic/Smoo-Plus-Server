using System.Text;

namespace Shared;

public class Logger (string name) {
    private readonly List<string> _buffer = new();
    private readonly Lock _lock = new();
    private static readonly List<string> GlobalBuffer = new();
    private static readonly Lock GlobalLock = new();

    public string Name = name;

    public void Info(string text) => WriteAndHandle("Info", text, ConsoleColor.White);

    public void Warn(string text) => WriteAndHandle("Warn", text, ConsoleColor.Yellow);

    public void Error(string text) => WriteAndHandle("Error", text, ConsoleColor.Red);

    public void Error(Exception error) => Error(error.ToString());

    private void WriteAndHandle(string level, string text, ConsoleColor color)
    {
        lock (_lock)
        {
            foreach (var line in text.Split('\n'))
            {
                _buffer.Add($"[{DateTime.Now}] {level} [{Name}] {line}");
                if (_buffer.Count > 10000)
                    _buffer.RemoveAt(0);
            }
        }

        // Also append to global buffer
        lock (GlobalLock)
        {
            foreach (var line in text.Split('\n'))
            {
                GlobalBuffer.Add($"[{DateTime.Now}] {level} [{Name}] {line}");
                if (GlobalBuffer.Count > 10000)
                    GlobalBuffer.RemoveAt(0);
            }
        }
        _handler?.Invoke(Name, level, text, color);
    }

    public string GetOutput()
    {
        lock (_lock)
        {
            return string.Join(Environment.NewLine, _buffer);
        }
    }

    public static string PrefixNewLines(string text, string prefix) {
        StringBuilder builder = new StringBuilder();
        foreach (string str in text.Split('\n'))
            builder
                .Append(prefix)
                .Append(' ')
                .AppendLine(str);
        return builder.ToString();
    }

    public static string GetGlobalOutput()
    {
        lock (GlobalLock)
        {
            return string.Join(Environment.NewLine, GlobalBuffer);
        }
    }

    public delegate void LogHandler(string source, string level, string text, ConsoleColor color);

    private static LogHandler? _handler;
    public static void AddLogHandler(LogHandler handler) => _handler += handler;

    static Logger() {
        AddLogHandler((source, level, text, color) => {
            Console.ForegroundColor = color;
            Console.Write(PrefixNewLines(text, $"{{{DateTime.Now}}} {level} [{source}]"));
        });
    }
}