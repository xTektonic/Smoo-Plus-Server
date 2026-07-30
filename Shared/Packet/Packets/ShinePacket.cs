using System.Runtime.InteropServices;

namespace Shared.Packet.Packets;

[Packet(PacketType.ShineColl)]
public struct ShinePacket() : IPacket
{
    public int ShineId = 0;

    public short Size => sizeof(int);

    public void Serialize(Span<byte> data)
    {
        MemoryMarshal.Write(data, in ShineId);
    }

    public void Deserialize(ReadOnlySpan<byte> data)
    {
        ShineId = MemoryMarshal.Read<int>(data);
    }
}