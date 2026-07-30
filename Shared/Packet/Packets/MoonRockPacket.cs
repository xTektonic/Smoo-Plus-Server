using System.Runtime.InteropServices;

namespace Shared.Packet.Packets;

[Packet(PacketType.MoonRockHit)]
public struct MoonRockPacket() : IPacket
{
    public int WorldId = 0;
    
    public short Size => sizeof(int);

    public void Serialize(Span<byte> data)
    {
        MemoryMarshal.Write(data, in WorldId);
    }

    public void Deserialize(ReadOnlySpan<byte> data)
    {
        WorldId = MemoryMarshal.Read<int>(data);
    }
}