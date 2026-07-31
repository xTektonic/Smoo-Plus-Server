using System.Collections.Concurrent;
using System.Net;

namespace Server.JsonApi;

public static class BlockClients
{
    private const int MaxTries = 5;
    
    private static readonly ConcurrentDictionary<IPAddress, int> Failures = new ();

    public static bool IsBlocked(Context ctx) {
        if (ctx.Socket?.RemoteEndPoint == null) { return true; }

        IPAddress ip = (ctx.Socket.RemoteEndPoint as IPEndPoint)!.Address;

        int failures = Failures.GetValueOrDefault(ip, 0);
        return failures >= MaxTries;
    }


    public static void Fail(Context ctx) {
        if (ctx.Socket?.RemoteEndPoint == null) { return; }

        IPAddress ip = (ctx.Socket.RemoteEndPoint as IPEndPoint)!.Address;

        int failures = 1;
        Failures.AddOrUpdate(ip, 1, (_, v) => failures = v + 1);

        if (failures == MaxTries) {
            JsonApi.Logger.Warn($"Block client {ctx.Socket.RemoteEndPoint} because of too many failed requests.");
        }
    }


    public static void Redeem(Context ctx) {
        if (ctx.Socket?.RemoteEndPoint == null) { return; }

        IPAddress ip = (ctx.Socket?.RemoteEndPoint as IPEndPoint)!.Address;

        Failures.Remove(ip, out _);
    }
}
