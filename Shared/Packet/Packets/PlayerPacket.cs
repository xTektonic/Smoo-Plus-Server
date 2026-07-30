using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Shared.Packet.Packets;

[Packet(PacketType.PlayerInf)]
public struct PlayerPacket() : IPacket
{
    public Vector3 Position = default;
    public Quaternion Rotation = default;
    public float[] AnimationBlendWeights = Array.Empty<float>();
    public ushort Act = 0;
    public ushort SubAct = 0;
    
    public short Size => 0x38;

    public void Serialize(Span<byte> data)
    {
        MemoryMarshal.Write(data, in Position);
        MemoryMarshal.Write(data[12..], in Rotation);
        AnimationBlendWeights.CopyTo(MemoryMarshal.Cast<byte, float>(data[28..]));
        MemoryMarshal.Write(data[52..], in Act);
        MemoryMarshal.Write(data[54..], in SubAct);
    }

    public void Deserialize(ReadOnlySpan<byte> data)
    {
        Position = MemoryMarshal.Read<Vector3>(data);
        Rotation = MemoryMarshal.Read<Quaternion>(data[12..]);
        AnimationBlendWeights = MemoryMarshal.Cast<byte, float>(data[28..]).ToArray();
        Act = MemoryMarshal.Read<ushort>(data[52..]);
        SubAct = MemoryMarshal.Read<ushort>(data[54..]);
    }
}