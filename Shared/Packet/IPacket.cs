namespace Shared.Packet;

// Packet interface for type safety
public interface IPacket {
    short Size => 0;
    
    void Serialize(Span<byte> data) {}
    void Deserialize(ReadOnlySpan<byte> data){}
}