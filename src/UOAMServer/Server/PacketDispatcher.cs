using System.Text;
using UOAMServer.Domain;
using UOAMServer.Protocol;
using UOAMServer.Server.Handlers;

namespace UOAMServer.Server;

public sealed class PacketDispatcher
{
    private readonly Host _host;

    public PacketDispatcher(Host host)
    {
        _host = host;
    }

    public byte[]? Dispatch(Client client, byte[] data, int offset, PacketHeader header)
    {
        if (header.Type != PacketType.Data)
            return null; // Bind/Mystery are handled directly by UoamServer (Task 13), not routed here.

        const int bodyOffset = 16; // right after the 16-byte framing header
        const int minLength = 32; // stublen(4)+major(2)+minor(2)+password(20)+marker(4), i.e. up to the full 48-byte common prefix

        if ((int)header.Length < bodyOffset + minLength)
        {
            client.ShouldDestroy = true;
            return null;
        }

        var major = ReadUInt16LE(data, offset + bodyOffset + 4);
        var minor = ReadUInt16LE(data, offset + bodyOffset + 6);

        var passwordBytes = new byte[20];
        Array.Copy(data, offset + bodyOffset + 8, passwordBytes, 0, 20);
        var nulIndex = Array.IndexOf(passwordBytes, (byte)0);
        var password = Encoding.ASCII.GetString(passwordBytes, 0, nulIndex >= 0 ? nulIndex : 20);

        if (!TryLogin(client, password))
            return null;

        // Per-domain locking (robustness): this is the only place that locks
        // for the four handler calls below — UoamServer (Task 13) no longer
        // wraps Dispatch in a lock of its own.
        lock (client.Domain!.Lock)
        {
            return (major, minor) switch
            {
                // (0, 0) and (0, 3) are minors real uoam_Orion clients send
                // for a position update (byte-identical 140-byte payload to
                // (0, 1), confirmed from a live capture — only the minor
                // field differs, likely some client-side status flag this
                // server doesn't need to interpret). (0, 1) is kept too
                // since it's what the design doc's original (stock UOAM.exe)
                // capture recorded — all route to the same handler.
                (0, 0) or (0, 1) or (0, 3) => PositionHandler.Handle(client, data, offset, header.Sequence),
                (0, 2) => QueryListHandler.Handle(client, header.Sequence),
                (1, 1) => ChatPollHandler.Handle(client, header.Sequence),
                (1, 0) => ChatSendHandler.Handle(client, data, offset, header.Sequence),
                // major=2 is the ClassicUO extended-protocol overlay — see
                // docs/superpowers/specs/2026-08-10-classicuo-extension-design.md.
                // Opt-in only: a stock UOAM/uoam_Orion client never sends
                // these, so this is purely additive.
                (2, 0) => ExtendedCapabilityHandler.Handle(client, data, offset, header.Sequence),
                (2, 1) => ExtendedPositionHandler.Handle(client, data, offset, header.Sequence),
                (2, 3) => ExtendedPollHandler.Handle(client, header.Sequence),
                _ => null, // unrecognized but well-framed operation: drop the packet, keep the connection
            };
        }
    }

    private bool TryLogin(Client client, string password)
    {
        if (password.Length == 0)
        {
            client.ShouldDestroy = true;
            return false;
        }

        var currentDomain = client.Domain;
        if (client.Authorized && currentDomain is not null && currentDomain.Password == password)
            return true;

        lock (_host.Lock)
        {
            var domain = _host.GetDomainByPassword(password);
            if (domain is null)
            {
                if (_host.LockedPassword is not null && _host.LockedPassword != password)
                {
                    client.ShouldDestroy = true;
                    return false;
                }

                domain = _host.CreateDomain(password);
                if (domain is null)
                {
                    client.ShouldDestroy = true;
                    return false;
                }
            }

            var oldDomain = client.Domain;
            if (oldDomain is not null)
            {
                lock (oldDomain.Lock)
                    oldDomain.RemoveClient(client);
            }

            bool added;
            lock (domain.Lock)
                added = domain.TryAddClient(client);

            if (!added)
            {
                client.ShouldDestroy = true;
                return false;
            }
        }

        client.Authorized = true;
        return true;
    }

    private static ushort ReadUInt16LE(byte[] buffer, int offset) =>
        (ushort)(buffer[offset] | buffer[offset + 1] << 8);
}
