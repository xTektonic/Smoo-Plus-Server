using Shared;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace Server.JsonApi;

public class Context {
    public readonly Server Server;
    public readonly Socket? Socket;
    public readonly HttpListenerContext? HttpContext;
    public ApiRequest? Request;
    public Logger? Logger;

    // For socket-based context
    public Context(Server server, Socket socket) {
        this.Server = server;
        this.Socket = socket;
    }

    // For HTTP-based context
    public Context(Server server, HttpListenerContext httpContext) {
        this.Server = server;
        this.HttpContext = httpContext;
    }

    public bool HasPermission(string perm) {
        if (Request == null) { return false; }
        var permissions = Settings.JsonApi.Tokens[Request!.Token!];
        if (permissions.Contains(perm)) { return true; }

        string current = perm;
        while (true) {
            if (permissions.Contains($"{current}/*")) { return true; }
            int lastSlash = current.LastIndexOf('/');
            if (lastSlash < 0) { break; }
            current = current[..lastSlash];
            
        }

        return false;
    }

    public SortedSet<string> Permissions {
        get {
            if (Request == null) { return new SortedSet<string>(); }
            return Settings.JsonApi.Tokens[Request!.Token!];
        }
    }

    public async Task Send(object data) {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(data);
        
        if (Socket != null) {
            // Handle socket response
            await Socket.SendAsync(bytes, SocketFlags.None);
        } else if (HttpContext != null) {
            // Handle HTTP response
            HttpContext.Response.ContentType = "application/json";
            HttpContext.Response.ContentLength64 = bytes.Length;
            await HttpContext.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            HttpContext.Response.OutputStream.Close();
        } else {
            throw new InvalidOperationException("No valid context available for sending response");
        }
    }
}
