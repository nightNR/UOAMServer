using System.Net;
using System.Net.Sockets;
using UOAMServer.Domain;
using UOAMServer.Protocol;
using UOAMServer.Server.Handlers;

namespace UOAMServer.Server;

public sealed class UoamServer
{
    private readonly Host _host;
    private readonly int _port;
    private readonly PacketDispatcher _dispatcher;
    private readonly ConnectionLimits _connectionLimits = new();
    private uint _nextClientId = 1;

    private readonly object _connectionsLock = new();
    private readonly List<Task> _connectionTasks = new();

    private const int ChatBindRequestLength = 72;
    private const int ChatFamilyMajor = 1;
    private const int ChatFamilyMinorSend = 0;
    private const int MajorFieldOffset = 20; // header(16) + major(offset 4-5 of the 48-byte data prefix)
    private const int MinorFieldOffset = MajorFieldOffset + 2; // = 22
    private const int MinMajorFieldLength = MajorFieldOffset + 2; // = 22: enough of the packet to safely read major
    private const int IdleTimeoutSeconds = 60;
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ShutdownDrainTimeout = TimeSpan.FromSeconds(5);

    public UoamServer(Host host, int port)
    {
        _host = host;
        _port = port;
        _dispatcher = new PacketDispatcher(host);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Dual-stack (IPv6 socket with IPv4-mapped addresses) so "localhost"
        // clients that try ::1 first (the common Windows resolver order)
        // connect immediately instead of exhausting several IPv6 SYN
        // retries against a port nothing is listening on and only then
        // falling back to 127.0.0.1 — that fallback alone was adding a
        // multi-second delay before the client became responsive.
        var listener = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp);
        listener.DualMode = true;
        // No SO_REUSEADDR: on Windows it lets a second process silently
        // steal a port an earlier instance is still actively listening on
        // (unlike Linux's TIME_WAIT-only reuse) — connections then land on
        // whichever process the OS feels like, with no error to either side.
        // A stale instance still holding the port should fail this bind loudly.
        listener.Bind(new IPEndPoint(IPAddress.IPv6Any, _port));
        listener.Listen(4);
        Log.Debug($"listening on [::]:{_port} (dual-stack)");

        _ = RunMaintenanceSweepAsync(cancellationToken);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Socket socket;
                try
                {
                    socket = await listener.AcceptAsync(cancellationToken);
                }
                catch (SocketException ex)
                {
                    // Robustness: a transient accept failure must not stop the
                    // server from accepting further connections.
                    Log.Warn($"accept failed, continuing: {ex.Message}");
                    continue;
                }

                Log.Debug($"accepted connection from {socket.RemoteEndPoint}");

                var connectionTask = HandleConnectionAsync(socket, cancellationToken);
                lock (_connectionsLock)
                {
                    _connectionTasks.Add(connectionTask);
                    _connectionTasks.RemoveAll(t => t.IsCompleted);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown requested
        }
        finally
        {
            listener.Close();

            Task[] pending;
            lock (_connectionsLock)
                pending = _connectionTasks.Where(t => !t.IsCompleted).ToArray();

            if (pending.Length > 0)
            {
                // Robustness: graceful shutdown drain, bounded so a stuck
                // peer can't hang shutdown indefinitely.
                Log.Info($"draining {pending.Length} connection(s), up to {ShutdownDrainTimeout.TotalSeconds}s");
                await Task.WhenAny(Task.WhenAll(pending), Task.Delay(ShutdownDrainTimeout));
            }
        }
    }

    private async Task RunMaintenanceSweepAsync(CancellationToken cancellationToken)
    {
        // Robustness: replaces the original plan's client-timeout sweep
        // (idle timeout is now per-connection, see HandleConnectionAsync)
        // with reaping empty, non-DomainZero domains — Domain.IsEmpty and
        // Host.RemoveDomain existed since Task 5 but nothing ever called
        // them until now.
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(MaintenanceInterval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            lock (_host.Lock)
            {
                foreach (var domain in _host.Domains.ToList())
                {
                    if (domain == _host.DomainZero)
                        continue;

                    bool empty;
                    lock (domain.Lock)
                        empty = domain.IsEmpty;

                    if (empty)
                        _host.RemoveDomain(domain);
                }
            }
        }
    }

    private async Task HandleConnectionAsync(Socket socket, CancellationToken cancellationToken)
    {
        var remoteAddress = (socket.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None;
        if (!_connectionLimits.TryAcquire(remoteAddress))
        {
            // Robustness: rejected before any protocol response — this
            // connection never gets far enough to earn a BindNak, which
            // would imply "you sent something we could parse and reject".
            Log.Warn($"rejected connection from {remoteAddress}: per-address limit reached");
            socket.Close();
            return;
        }

        uint clientId;
        lock (_host.Lock)
            clientId = _nextClientId++;

        Log.Debug($"client {clientId}: assigned, remote={remoteAddress}");

        // Every connection starts out owning a fresh, provisional Client —
        // most (position/primary connections) keep it for the rest of their
        // life. If the first Bind turns out to be a chat-bind handshake
        // instead, ProcessChatBind below discards this provisional Client
        // (removed from its domain) and reassigns `client` to the *existing*
        // Client the chat-bind resolves, flipping `isChatConnection` and
        // clearing `ownsClientLifecycle` — see the spec's "Connection
        // topology" section. Neither flag nor `client` itself is ever stored
        // on the Client; both are purely local to this loop.
        var client = new Client(clientId);
        var isChatConnection = false;
        var ownsClientLifecycle = true;

        lock (_host.DomainZero!.Lock)
            _host.DomainZero.TryAddClient(client);

        // Robustness: sized to Protocol.PacketLimits.MaxPacketLength — named
        // PacketLimits (not Limits) specifically so it doesn't collide with
        // Domain.Limits, also in scope via the `using UOAMServer.Domain;`
        // above. Up from a fixed 4096 — the buffer must be able to hold the
        // largest packet TryParse now allows through, or a legitimate large
        // chat message would get rejected by buffer exhaustion, not accepted.
        var buffer = new byte[PacketLimits.MaxPacketLength];
        var bufferedLength = 0;

        // Robustness: idle timeout is a per-connection CancellationTokenSource
        // whose deadline is pushed out on every processed packet, instead of a
        // shared Client.TimeoutAt field a periodic sweep used to check — that
        // design couldn't interrupt a connection already blocked inside
        // ReceiveAsync, so an idle-past-budget socket could sit forever.
        using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        idleCts.CancelAfter(TimeSpan.FromSeconds(IdleTimeoutSeconds));

        try
        {
            while (!idleCts.IsCancellationRequested && !client.ShouldDestroy)
            {
                var received = await socket.ReceiveAsync(buffer.AsMemory(bufferedLength), idleCts.Token);
                if (received == 0)
                {
                    Log.Debug($"client {clientId}: peer closed (0-byte receive)");
                    break;
                }

                bufferedLength += received;

                var consumed = 0;
                while (true)
                {
                    if (!PacketHeader.TryParse(buffer, consumed, bufferedLength - consumed, out var header))
                        break;
                    if (bufferedLength - consumed < (int)header.Length)
                        break; // wait for more bytes

                    Log.Debug($"client {clientId}: recv type={header.Type} length={header.Length} seq={header.Sequence}");
                    Log.Debug($"client {clientId}: recv bytes: {ToHex(buffer, consumed, (int)header.Length)}");

                    var response = ProcessPacket(ref client, ref isChatConnection, ref ownsClientLifecycle, buffer, consumed, header);
                    if (response is not null)
                        await socket.SendAsync(response, idleCts.Token);

                    Log.Debug($"client {clientId}: response={(response is null ? "none" : $"{response.Length} bytes")} shouldDestroy={client.ShouldDestroy}");
                    if (response is not null)
                        Log.Debug($"client {clientId}: response bytes: {ToHex(response, 0, response.Length)}");

                    if (client.ShouldDestroy)
                        break;

                    idleCts.CancelAfter(TimeSpan.FromSeconds(IdleTimeoutSeconds)); // reset idle budget on activity
                    consumed += (int)header.Length;
                }

                var remaining = bufferedLength - consumed;
                Array.Copy(buffer, consumed, buffer, 0, remaining);
                bufferedLength = remaining;

                if (bufferedLength == buffer.Length)
                {
                    // Robustness: since the buffer is sized to exactly
                    // MaxPacketLength and TryParse already rejects any
                    // declared length above that, a full buffer without a
                    // completed packet can only mean malformed/misaligned
                    // data — never a legitimate packet still arriving (the
                    // largest legitimate one exactly fills the buffer only
                    // once it's already complete and consumed). Close rather
                    // than call ReceiveAsync with a zero-length span, which
                    // would return 0 immediately and be misread as a clean
                    // disconnect even though the peer is still connected.
                    Log.Warn($"client {clientId}: receive buffer full without a complete packet, closing");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown requested, or this connection's own idle timeout fired
            Log.Debug($"client {clientId}: cancelled (shutdown or idle timeout after {IdleTimeoutSeconds}s)");
        }
        catch (SocketException ex)
        {
            // peer reset/closed abnormally — treat like a normal disconnect
            Log.Debug($"client {clientId}: socket exception, closing: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Robustness: an unexpected bug surfaced by this connection's
            // traffic must not escape as an unobserved task exception —
            // log it and close just this connection.
            Log.Warn($"client {clientId}: unhandled exception, closing: {ex}");
        }
        finally
        {
            // Only the connection that created this Client (its position/
            // primary handshake) removes it from its domain on close. A chat
            // connection that resolved an *existing* Client via master_id
            // must not — that Client is still owned by its own position
            // connection's loop, which may still be open.
            Log.Debug($"client {clientId}: closing, ownsClientLifecycle={ownsClientLifecycle} isChatConnection={isChatConnection} domain={(client.Domain is null ? "none" : "set")}");

            if (ownsClientLifecycle && client.Domain is not null)
            {
                lock (client.Domain.Lock)
                    client.Domain.RemoveClient(client);
            }

            socket.Close();
            _connectionLimits.Release(remoteAddress);
        }
    }

    private byte[]? ProcessPacket(ref Client client, ref bool isChatConnection, ref bool ownsClientLifecycle,
        byte[] buffer, int offset, PacketHeader header)
    {
        switch (header.Type)
        {
            case PacketType.Bind:
                if ((int)header.Length == ChatBindRequestLength)
                    return ProcessChatBind(ref client, ref isChatConnection, ref ownsClientLifecycle, buffer, offset, header);

                if (!HandshakeHandler.IsValidPositionRequest(buffer, offset, (int)header.Length))
                {
                    Log.Debug($"client {client.Id}: position bind rejected (invalid request), sending Nak");
                    return HandshakeHandler.BuildNak(header.Sequence);
                }

                client.Handshaken = true;
                Log.Debug($"client {client.Id}: position bind accepted, sending Ack");
                return HandshakeHandler.BuildAck(client, header.Sequence, _port);

            case PacketType.Mystery:
                return MysteryHandler.BuildResponse(client, header.Sequence);

            case PacketType.Data:
                if (!client.Handshaken)
                {
                    client.ShouldDestroy = true;
                    return null;
                }

                if ((int)header.Length >= MinMajorFieldLength)
                {
                    var major = ReadUInt16LE(buffer, offset + MajorFieldOffset);
                    if (major == ChatFamilyMajor && !isChatConnection && !IsChatSendFamily(buffer, offset, (int)header.Length))
                    {
                        Log.Debug($"client {client.Id}: chat-family packet on non-chat connection, sending fault");
                        return FaultTemplate.Build(header.Sequence);
                    }
                }

                // No lock here: PacketDispatcher.Dispatch (Task 12) manages
                // its own per-domain locking internally now — this is the
                // behavior change from the original plan's single
                // lock(_host.Lock) wrap around every dispatch call.
                return _dispatcher.Dispatch(client, buffer, offset, header);

            default:
                client.ShouldDestroy = true;
                return null;
        }
    }

    private byte[]? ProcessChatBind(ref Client client, ref bool isChatConnection, ref bool ownsClientLifecycle,
        byte[] buffer, int offset, PacketHeader header)
    {
        if (!HandshakeHandler.IsValidChatBindRequest(buffer, offset, (int)header.Length))
        {
            Log.Debug($"client {client.Id}: chat bind rejected (invalid request), sending Nak");
            return HandshakeHandler.BuildNak(header.Sequence);
        }

        HandshakeHandler.TryGetMasterId(buffer, offset, out var masterId);
        Log.Debug($"client {client.Id}: chat bind requests master_id={masterId}");

        Client? master;
        lock (_host.Lock)
            master = _host.FindClientById(masterId);

        if (master is null)
        {
            // No capture of this exact case exists; mirrors uoamhub.c's own
            // precedent for an unresolvable master id — drop the connection,
            // no ack. See design spec's "Handshake" section.
            Log.Debug($"client {client.Id}: chat bind master_id={masterId} not found, destroying connection (no ack)");
            client.ShouldDestroy = true;
            return null;
        }

        // This connection's own provisional Client is discarded — it's
        // always still sitting in DomainZero (a chat-bind is only ever the
        // very first packet on a brand-new connection, so it never logged
        // into any password room yet). It never became visible (no position
        // update ever landed on it) and this loop no longer owns its
        // lifecycle once it switches to `master`.
        lock (_host.DomainZero!.Lock)
            _host.DomainZero.RemoveClient(client);

        client = master;
        isChatConnection = true;
        ownsClientLifecycle = false;
        client.Handshaken = true;

        Log.Debug($"client {client.Id}: chat bind resolved to master, sending Ack");
        return HandshakeHandler.BuildAck(client, header.Sequence, _port);
    }

    // Real uoam_Orion clients send a chat "registration" packet on the
    // position/primary connection before ever opening the dedicated
    // chat-bind connection — faulting it (the doc's assumption, based on a
    // different client family) makes this client tear the whole connection
    // down and retry forever, never settling into a working chat session.
    // Its exact payload shape isn't stable (observed both an 88-byte and a
    // 108-byte variant with the subtype at different offsets), so rather
    // than pattern-match the payload, exempt the whole (major=1, minor=0)
    // "send" family from the fault gate on non-chat connections — real chat
    // text sends only ever arrive here on an already-bound chat connection
    // (where isChatConnection is already true and this gate doesn't apply
    // at all), so this can't misroute an actual chat message. ChatSendHandler
    // already no-ops registration (subtype 2) and defensively handles
    // whatever else lands here.
    private static bool IsChatSendFamily(byte[] buffer, int offset, int length)
    {
        if (length < MinorFieldOffset + 2)
            return false;

        return ReadUInt16LE(buffer, offset + MinorFieldOffset) == ChatFamilyMinorSend;
    }

    private static ushort ReadUInt16LE(byte[] buffer, int offset) =>
        (ushort)(buffer[offset] | buffer[offset + 1] << 8);

    private static string ToHex(byte[] buffer, int offset, int length) =>
        Convert.ToHexString(buffer, offset, length);
}
