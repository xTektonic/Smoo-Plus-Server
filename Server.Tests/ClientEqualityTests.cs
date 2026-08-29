using System.Net.Sockets;
using Xunit;

namespace Server.Tests;

public sealed class ClientEqualityTests
{
    [Fact]
    public void NullComparisonsFollowNormalEqualitySemantics()
    {
        Client? first = null;
        Client? second = null;

        Assert.True(first == second);
        Assert.False(first != second);

        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        using var client = new Client(socket);

        Assert.False(client == null);
        Assert.True(client != null);
        Assert.False(null == client);
        Assert.True(null != client);
    }
}
