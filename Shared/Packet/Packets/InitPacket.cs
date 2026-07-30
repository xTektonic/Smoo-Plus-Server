using System.Runtime.InteropServices;
using System.Text;

namespace Shared.Packet.Packets;

[Packet(PacketType.ClientInit)]
public struct InitPacket() : IPacket
{
    public ushort MaxPlayers = 8;

    public short Size => sizeof(ushort);

    public void Serialize(Span<byte> data)
    {
        MemoryMarshal.Write(data, in MaxPlayers);
    }

    public void Deserialize(ReadOnlySpan<byte> data)
    {
        MaxPlayers = MemoryMarshal.Read<ushort>(data);
    }
}
