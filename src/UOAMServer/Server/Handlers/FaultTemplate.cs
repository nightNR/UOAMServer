using UOAMServer.Protocol;

namespace UOAMServer.Server.Handlers;

public static class FaultTemplate
{
    // Captured against a real hub for the "reconnect too soon" fault cause;
    // this project reuses the same byte pattern for the "chat on the wrong
    // connection" cause it actually implements (see design spec's "Faults"
    // section) — the status code at offset 24-27 wasn't independently
    // captured for that cause, but any 0x03 packet is treated as fatal
    // client-side regardless of status code, so reusing a real one is safe.
    private static readonly byte[] Template =
    {
        0x05, 0x00, 0x03, 0x03, 0x10, 0x00, 0x00, 0x00,
        0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x20, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x0b, 0x00, 0x01, 0x1c, 0x00, 0x00, 0x00, 0x00,
    };

    public static byte[] Build(uint sequence)
    {
        var response = (byte[])Template.Clone();
        // Fault (0x03) is not DataResponse (0x02), so ResponseHeader.Finalize
        // intentionally leaves offset 16-19 alone — it's already correct
        // (0x20 = 32 = this template's own fixed total length) in Template.
        return ResponseHeader.Finalize(response, sequence);
    }
}
