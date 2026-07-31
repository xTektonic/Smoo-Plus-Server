using System.Net.Sockets;

using System.Text;
using Newtonsoft.Json;
using Shared;

namespace Server.JsonApi;

public class ApiPacket {
    public const ushort MaxPacketSize = 512; // in bytes (including 20 byte header)

    [JsonProperty("API_JSON_REQUEST")]
    public ApiRequest? ApiJsonRequest { get; set; }

    public static async Task<ApiPacket?> Read(Context ctx, string header) {
        string reqStr = header + await GetRequestStr(ctx);

        ApiPacket? p;
        try { p = JsonConvert.DeserializeObject<ApiPacket>(reqStr); }
        catch {
            JsonApi.Logger.Warn($"Invalid packet deserialize from {ctx.Socket?.RemoteEndPoint}: {reqStr}.");
            return null;
        }

        if (p == null) {
            JsonApi.Logger.Warn($"Invalid packet from {ctx.Socket?.RemoteEndPoint}: {reqStr}.");
            return null;
        }

        if (p.ApiJsonRequest == null) {
            JsonApi.Logger.Warn($"Invalid request from {ctx.Socket?.RemoteEndPoint}: {reqStr}.");
            return null;
        }

        return p;
    }
    
    private static async Task<string> GetRequestStr(Context ctx) {
        byte[] buffer = new byte[MaxPacketSize - Constants.HeaderSize];
        int size = await ctx.Socket!.ReceiveAsync(buffer, SocketFlags.None);
        return Encoding.UTF8.GetString(buffer, 0, size);
    }
}

