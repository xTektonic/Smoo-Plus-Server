using System.Runtime.InteropServices;
using System.Text;

namespace Shared.Packet.Packets;
[Packet(PacketType.CoinCollectColl)]
public struct CoinCollectCollPacket() : IPacket
{
    private const int IdSize = 0x40;
    private const int StageSize = 0x40;
    
    public string PlaceId = string.Empty;
    public string Stage = string.Empty;
    public int WorldId = 0;
    
    public short Size => IdSize+StageSize+sizeof(int);
    
    public void Serialize(Span<byte> data) {
        Encoding.UTF8.GetBytes(PlaceId).CopyTo(data[..IdSize]);
        Encoding.UTF8.GetBytes(Stage).CopyTo(data[IdSize..(IdSize + StageSize)]);
        MemoryMarshal.Write(data[(IdSize + StageSize)..], WorldId);
        
    }
    public void Deserialize(ReadOnlySpan<byte> data) {
        PlaceId = Encoding.UTF8.GetString(data[..IdSize]).TrimNullTerm();
        Stage = Encoding.UTF8.GetString(data[IdSize..(IdSize + StageSize)]).TrimNullTerm();
        WorldId = BitConverter.ToInt32(data[(IdSize + StageSize)..(IdSize + StageSize+4)]);
        

    }
}