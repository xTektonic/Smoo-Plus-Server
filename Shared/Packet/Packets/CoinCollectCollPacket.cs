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
        MemoryMarshal.Write(data[IdSize..(IdSize+4)], WorldId);
        Encoding.UTF8.GetBytes(Stage).CopyTo(data[(IdSize + 4)..(IdSize + 4 + StageSize)]);
        
    }
    public void Deserialize(ReadOnlySpan<byte> data) {
        PlaceId = Encoding.UTF8.GetString(data[..IdSize]).TrimNullTerm();
        WorldId = BitConverter.ToInt32(data[(IdSize)..(IdSize + 4)]);
        Stage = Encoding.UTF8.GetString(data[(IdSize + 4)..(IdSize + 4 + StageSize)]).TrimNullTerm();
        

    }
}