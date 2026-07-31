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
        Encoding.UTF8.GetBytes(BodyName ?? "").CopyTo(data[..Constants.CostumeNameSize]);
        Encoding.UTF8.GetBytes(CapName ?? "").CopyTo(data[Constants.CostumeNameSize..(Constants.CostumeNameSize * 2)]);
    }

    public void Deserialize(ReadOnlySpan<byte> data)
    {
        BodyName = Encoding.UTF8.GetString(data.Slice(0, Constants.CostumeNameSize)).TrimNullTerm();
        CapName = Encoding.UTF8.GetString(data.Slice(Constants.CostumeNameSize, Constants.CostumeNameSize)).TrimNullTerm();
    }
}
