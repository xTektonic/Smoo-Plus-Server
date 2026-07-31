using System;
using System.Text;

namespace Shared.Packet.Packets;

[Packet(PacketType.CostumeInf)]
public struct CostumePacket() : IPacket
{
    
    public string BodyName = string.Empty;  
    public string CapName = string.Empty;   

    public short Size => Constants.CostumeNameSize * 2;

    public void Serialize(Span<byte> data)
    {
        Encoding.ASCII.GetBytes(BodyName ?? "").CopyTo(data[..Constants.CostumeNameSize]);
        Encoding.ASCII.GetBytes(CapName ?? "").CopyTo(data[Constants.CostumeNameSize..(Constants.CostumeNameSize * 2)]);
    }

    public void Deserialize(ReadOnlySpan<byte> data)
    {
        BodyName = Encoding.ASCII.GetString(data.Slice(0, Constants.CostumeNameSize)).TrimNullTerm();
        CapName = Encoding.ASCII.GetString(data.Slice(Constants.CostumeNameSize, Constants.CostumeNameSize)).TrimNullTerm();
    }
}
