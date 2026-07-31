using System.Buffers;
using System.Net.Sockets;
using System.Text;
using Shared;
using Shared.Packet;

namespace Server.JsonApi;


public static class JsonApi {
    private const ushort PacketType = 0x5453; // ascii "ST" (0x53 0x54) from preamble, but swapped because of endianness
    private const string Preamble = "{\"API_JSON_REQUEST\":";


    public static readonly Logger Logger = new ("JsonApi");


    public static async Task<bool> HandleApiRequest(
        Server server,
        Socket socket,
        PacketHeader header,
        IMemoryOwner<byte> memory
    ) {
        // check if it is enabled
        if (!Settings.Instance.JsonApi.Enabled) {
            return false;
        }

        // check packet type
        if ((ushort) header.Type != PacketType) {
            return false;
        }

        // check entire header length
        string headerStr = Encoding.UTF8.GetString(memory.Memory.Span[..Constants.HeaderSize].ToArray());
        if (headerStr != Preamble) {
            return false;
        }

        Context ctx = new Context(server, socket);

        // not if there were too many failed attempts in the past
        if (BlockClients.IsBlocked(ctx)) {
            Logger.Info($"Rejected blocked client {socket.RemoteEndPoint}.");
            return true;
        }
        
        Logger.Info($"Received JSON API request from {socket.RemoteEndPoint}.");

        // receive & parse JSON
        ApiPacket? p = await ApiPacket.Read(ctx, headerStr);
        if (p == null) {
            BlockClients.Fail(ctx);
            return true;
        }

        // verify basic request structure & token
        ApiRequest req = p.ApiJsonRequest!;
        ctx.Request = req;
        if (!req.IsValid(ctx)) {
            BlockClients.Fail(ctx);
            return true;
        }

        // process request
        if (!await req.Process(ctx)) {
            BlockClients.Fail(ctx);
            return true;
        }

        BlockClients.Redeem(ctx);
        return true;
    }
}
