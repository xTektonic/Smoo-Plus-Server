namespace Shared.Packet;

public enum PacketType : short
{
    Unknown, // = 0
    ClientInit, // = 1
    PlayerInf, // = 2
    CapInf, // = 3
    GameInf,   // = 4
    TagInf, // = 5
    PlayerConnect, // = 6
    PlayerDisconnect, // = 7
    CostumeInf, // = 8
    ShineColl, // = 9
    CaptureInf, // = 10
    ChangeStage, // = 11
    Command, // = 12
    CoinCollectColl, // = 13
    CheckpointGet, // = 14
    MoonRockHit, // = 15
    GameStart, // = 16
}

[AttributeUsage(AttributeTargets.Struct, AllowMultiple = true)]
public class PacketAttribute(PacketType type) : Attribute {
    public readonly PacketType Type = type;
}

// Empty Packets, only here to prevent error in console

[Packet(PacketType.Unknown)]
public struct UnhandledPacket : IPacket;

[Packet(PacketType.GameStart)]
public struct GameStartPacket : IPacket;

[Packet(PacketType.PlayerDisconnect)]
public struct DisconnectPacket : IPacket;

[Packet(PacketType.Command)]
public struct CommandPacket : IPacket;
