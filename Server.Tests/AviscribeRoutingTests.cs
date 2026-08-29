using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Server;
using Server.AviscribeApi;
using Shared.Packet;
using Shared.Packet.Packets;
using Xunit;

namespace Server.Tests;

public sealed class AviscribeRoutingTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"aviscribe-routing-{Guid.NewGuid():N}");
    private CancellationTokenSource _cancellation = null!;
    private Task _serverTask = null!;
    private global::Server.Server _server = null!;
    private int _port;
    private int _packetCalls;
    private int _joinCalls;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        _port = GetFreePort();
        Settings.Server.Address = IPAddress.Loopback.ToString();
        Settings.Server.Port = checked((ushort)_port);
        Settings.Server.MaxPlayers = 8;
        Settings.JsonApi.Enabled = false;
        Settings.Aviscribe.Enabled = true;
        Settings.Aviscribe.StateFilename = Path.Combine(_directory, "runs.json");
        Settings.Aviscribe.WaitTimeoutSeconds = 1;
        _server = new global::Server.Server();
        _server.PacketHandler = (_, _) => { Interlocked.Increment(ref _packetCalls); return false; };
        _server.ClientJoined += (_, _) => Interlocked.Increment(ref _joinCalls);
        _cancellation = new CancellationTokenSource();
        _serverTask = _server.Listen(_cancellation.Token);
        await WaitUntilListeningAsync();
    }

    [Fact]
    public async Task FragmentedAviscribeRequestIsClaimedBeforeGameClientConstruction()
    {
        var response = await SendAviscribeAsync(new
        {
            version = 1,
            requestId = Guid.NewGuid(),
            operation = "capabilities"
        }, fragmentSize: 1);
        Assert.True(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(response.RootElement.GetProperty("data").GetProperty("enabled").GetBoolean());
        Assert.Empty(_server.Clients);
        Assert.Equal(0, _packetCalls);
        Assert.Equal(0, _joinCalls);
    }

    [Fact]
    public async Task RecognizedTrafficReturnsStructuredErrorWhenFeatureIsDisabled()
    {
        Settings.Aviscribe.Enabled = false;
        var response = await SendAviscribeAsync(new
        {
            version = 1,
            requestId = Guid.NewGuid(),
            operation = "createRun",
            data = new { }
        });
        Assert.False(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("featureDisabled", response.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(_server.Clients);
    }

    [Fact]
    public async Task MalformedUnsupportedAndOversizedFramesReturnErrorsWithoutGameHandling()
    {
        var malformed = await SendRawAsync([1, 2, 3]);
        Assert.Equal("invalidRequest", malformed.RootElement.GetProperty("error").GetProperty("code").GetString());

        var unsupported = await SendAviscribeAsync(new
        {
            version = 99,
            requestId = Guid.NewGuid(),
            operation = "capabilities"
        });
        Assert.Equal("unsupportedVersion", unsupported.RootElement.GetProperty("error").GetProperty("code").GetString());

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        await using var stream = client.GetStream();
        await stream.WriteAsync(AviscribeProtocol.Magic);
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, AviscribeProtocol.MaximumRequestSize + 1);
        await stream.WriteAsync(length);
        var oversized = await ReadResponseAsync(stream);
        Assert.Equal("invalidRequest", oversized.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Empty(_server.Clients);
        Assert.Equal(0, _packetCalls);
    }

    [Fact]
    public async Task NormalGameHandshakeStillCreatesExactlyOneGameClient()
    {
        Settings.Aviscribe.Enabled = true;
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        await using var stream = client.GetStream();
        var connect = new ConnectPacket
        {
            ConnectionType = ConnectPacket.ConnectionTypes.FirstConnection,
            ClientName = "Routing Test"
        };
        var header = new PacketHeader
        {
            Id = Guid.NewGuid(),
            Type = PacketType.PlayerConnect,
            PacketSize = connect.Size
        };
        var frame = new byte[Shared.Constants.HeaderSize + connect.Size];
        header.Serialize(frame.AsSpan(0, Shared.Constants.HeaderSize));
        connect.Serialize(frame.AsSpan(Shared.Constants.HeaderSize));
        await stream.WriteAsync(frame);

        var initHeader = new byte[Shared.Constants.HeaderSize];
        await ReadExactAsync(stream, initHeader);
        var parsed = new PacketHeader();
        parsed.Deserialize(initHeader);
        Assert.Equal(PacketType.ClientInit, parsed.Type);
        var initBody = new byte[parsed.PacketSize];
        await ReadExactAsync(stream, initBody);

        await WaitForAsync(() => _server.Clients.Count(item => item.Connected) == 1);
        Assert.Equal(1, _joinCalls);
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        try { await _serverTask; } catch (OperationCanceledException) { }
        _cancellation.Dispose();
        foreach (var client in _server.Clients.ToArray()) client.Dispose();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private async Task<JsonDocument> SendAviscribeAsync(object request, int fragmentSize = int.MaxValue)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(request, AviscribeProtocol.JsonOptions);
        return await SendRawAsync(payload, fragmentSize);
    }

    private async Task<JsonDocument> SendRawAsync(byte[] payload, int fragmentSize = int.MaxValue)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        await using var stream = client.GetStream();
        var frame = new byte[AviscribeProtocol.Magic.Length + 4 + payload.Length];
        AviscribeProtocol.Magic.CopyTo(frame, 0);
        BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(AviscribeProtocol.Magic.Length, 4), payload.Length);
        payload.CopyTo(frame, AviscribeProtocol.Magic.Length + 4);
        for (var offset = 0; offset < frame.Length; offset += fragmentSize)
            await stream.WriteAsync(frame.AsMemory(offset, Math.Min(fragmentSize, frame.Length - offset)));
        return await ReadResponseAsync(stream);
    }

    private static async Task<JsonDocument> ReadResponseAsync(Stream stream)
    {
        var length = new byte[4];
        await ReadExactAsync(stream, length);
        var payload = new byte[BinaryPrimitives.ReadInt32BigEndian(length)];
        await ReadExactAsync(stream, payload);
        return JsonDocument.Parse(payload);
    }

    private static async Task ReadExactAsync(Stream stream, Memory<byte> buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..]);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
    }

    private async Task WaitUntilListeningAsync()
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync(IPAddress.Loopback, _port);
                return;
            }
            catch (SocketException)
            {
                await Task.Delay(20);
            }
        }
        throw new TimeoutException("The test server did not begin listening.");
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++) await Task.Delay(20);
        Assert.True(condition());
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
