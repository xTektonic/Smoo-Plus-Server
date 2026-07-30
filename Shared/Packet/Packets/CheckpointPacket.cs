using System.Text;

namespace Shared.Packet.Packets;

[Packet(PacketType.CheckpointGet)]
public struct CheckpointPacket() : IPacket
{
    public string ObjId = string.Empty;
    
    public short Size => 0x40;
    
    public void Serialize(Span<byte> data) {
        Encoding.UTF8.GetBytes(ObjId).CopyTo(data[..Size]);
    }
    public void Deserialize(ReadOnlySpan<byte> data) {
        ObjId = Encoding.UTF8.GetString(data[..Size]).TrimNullTerm();
    }
}