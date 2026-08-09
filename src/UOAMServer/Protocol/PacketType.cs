namespace UOAMServer.Protocol;

public enum PacketType : byte
{
    Data = 0x00,
    DataResponse = 0x02,
    Fault = 0x03,
    Bind = 0x0b,
    BindAck = 0x0c,
    BindNak = 0x0d,
    Mystery = 0x0e,
    MysteryAck = 0x0f,
}
