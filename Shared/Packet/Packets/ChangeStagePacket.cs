using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Shared.Packet.Packets;

[Packet(PacketType.ChangeStage)]
public struct ChangeStagePacket() : IPacket {
    private const int IdSize = 0x10;
    private const int StageSize = 0x30;
    
    public string Stage = string.Empty;
    public string Id = string.Empty;
    public sbyte Scenario = 0;
    public byte SubScenarioType = 0;
    
    public short Size => 0x42;
    
    public void Serialize(Span<byte> data) {
        Encoding.UTF8.GetBytes(Stage).CopyTo(data[..StageSize]);
        Encoding.UTF8.GetBytes(Id).CopyTo(data[StageSize..(IdSize + StageSize)]);
        MemoryMarshal.Write(data[(IdSize + StageSize)..], Scenario);
        MemoryMarshal.Write(data[(IdSize + StageSize + 1)..], SubScenarioType);
    }
    
    public void Deserialize(ReadOnlySpan<byte> data) {
        Stage = Encoding.UTF8.GetString(data[..StageSize]).TrimNullTerm();
        Id = Encoding.UTF8.GetString(data[StageSize..(IdSize + StageSize)]).TrimNullTerm();
        Scenario = MemoryMarshal.Read<sbyte>(data[(IdSize + StageSize)..(IdSize + StageSize + 1)]);
        SubScenarioType = MemoryMarshal.Read<byte>(data[(IdSize + StageSize + 1)..(IdSize + StageSize + 2)]);
    }
}