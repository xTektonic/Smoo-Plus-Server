using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Shared.Packet.Packets;

[Packet(PacketType.CapInf)]
public struct CapPacket() : IPacket
{
    public const int NameSize = 0x30;
    public Vector3 Position = default;
    public Quaternion Rotation = default;
    public bool CapOut = false;
    public string CapAnim =  string.Empty;
    
    public short Size => 0x50;

    public void Serialize(Span<byte> data)
    {
        MemoryMarshal.Write(data, in Position);
        MemoryMarshal.Write(data[12..], in Rotation);
        MemoryMarshal.Write(data[28..], in CapOut);
        Encoding.UTF8.GetBytes(CapAnim).CopyTo(data[32..(32 + NameSize)]);
    }

    public void Deserialize(ReadOnlySpan<byte> data)
    {
        Position = MemoryMarshal.Read<Vector3>(data);
        Rotation = MemoryMarshal.Read<Quaternion>(data[12..]);
        CapOut = MemoryMarshal.Read<bool>(data[28..]);
        CapAnim = Encoding.UTF8.GetString(data[32..(32 + NameSize)]).TrimEnd('\0');
    }
}