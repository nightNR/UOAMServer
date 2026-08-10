# UOAMServer wire protocol reference

Authoritative, implementation-level description of the wire protocol this
server speaks, for anyone writing a client (stock-UOAM-compatible or using
the ClassicUO extended overlay). Reverse-engineered from real client/hub
captures and confirmed against production traffic; sections below note
confidence level where it isn't 100%.

Two known implementations of the client side: `UOAM.exe` (original) and
`uoam_Orion.exe` (community fork) — both work against this server. A custom
ClassicUO-based client can additionally use the [extended overlay](#extended-protocol-overlay-classicuo)
described near the end.

## Framing

Every packet, both directions, starts with a fixed 16-byte header:

| Offset | Size | Field | Notes |
|---|---|---|---|
| 0 | 1 | `0x05` | magic |
| 1 | 1 | `0x00` | magic |
| 2 | 1 | packet type | see table below |
| 3 | 1 | `0x03` | magic |
| 4 | 1 | `0x10` | magic |
| 5-7 | 3 | `0x00 0x00 0x00` | magic |
| 8-11 | 4 | uint32 LE | total packet length, **including this header** |
| 12-15 | 4 | uint32 LE | sequence number — client picks it per request; server echoes it back verbatim on the response |

All multi-byte integers on the wire are little-endian. A single TCP `recv()`
may return more than one packet back to back, or a partial packet — frame by
reading the length field at offset 8 and buffering until you have that many
bytes.

Packet types (offset 2):

| Type | Direction | Meaning |
|---|---|---|
| `0x0b` | C→S | handshake / bind — **two different shapes**, see [Handshake](#handshake) |
| `0x0c` | S→C | handshake ack (bind_ack) |
| `0x0d` | S→C | handshake nak (bind_nak) — malformed/rejected handshake |
| `0x0e` | C→S | mandatory post-handshake packet (position connections only) |
| `0x0f` | S→C | response to `0x0e` |
| `0x00` | C→S | data request (position update / query list / chat poll / chat send / extended overlay) |
| `0x02` | S→C | data response (ack / no-chat / player-list / chat-data / extended-data) |
| `0x03` | S→C | fault |

This framing is genuinely Microsoft RPC (`RPCRT4.dll`), not just
RPC-*shaped* — the client binary imports `RpcBindingFromStringBindingA`,
`NdrClientCall2`, etc. The type byte matches real DCE/RPC PDU type numbers
(0=request, 2=response, 3=fault, 11=bind, 12=bind_ack, 13=bind_nak), and
strings inside packets use NDR conformant-string conventions (see
[NDR strings](#ndr-ish-strings)). This project's "major"/"minor" fields
(below) are very likely the real RPC request's `p_cont_id` and `opnum`.

## Connection topology — three TCP connections per player session

This is the single most important non-obvious fact about this protocol. A
real UOAM-family client, for one logged-in player, opens and holds open
**three simultaneous TCP connections**, not one:

1. **Two independent "position" connections**, both alive for the whole
   session. Each does its own full handshake (own client id, own sequence
   space) and independently sends the same position updates and query-list
   requests the entire time. They are **not** a primary/backup pair — each
   is a fully separate server-side identity that happens to represent the
   same player (same name, same password). A server must accept both; a
   client must open both to interoperate correctly with a real hub (and this
   server, which mirrors that behavior).
2. **One "chat" connection**, opened once via a *different, shorter*
   handshake shape that references one of the position connections' client
   id. **Chat poll and chat send only work on this connection.** A
   chat-family request sent on a position connection is accepted by this
   server (see the [connection-role note](#chat-family-requests-on-a-position-connection)
   below — this server is more lenient than a real reference hub here), but
   a real reference hub returns an outright RPC [Fault](#faults) for it, so
   a well-behaved client should not rely on that leniency and should route
   chat traffic to its dedicated chat connection.

A client implementation should open all three connections at session start
and route requests accordingly: position updates + query-list polling on
either/both position connections, chat poll + chat send on the chat
connection only.

## Handshake

Two different request shapes share type `0x0b`; which one a given request is
gets decided by its declared total length.

### Position/primary handshake (~116 bytes declared, `0x74`)

Not a fixed byte-exact template — validate structurally: declared length
`>= 24`, and the 16-byte interface GUID
`10 66 15 9e 5c 7b d2 11 b8 cf 00 80 c7 97 1b e1` present somewhere in the
payload. (Some real clients send the *chat* interface GUID here instead —
see below — so the server accepts either GUID; a conforming client should
send the position GUID above.)

Ack response (`0x0c`, S→C), fixed 60 bytes:

```
offset 0-15    standard header (type=0x0c)
offset 16-19   0x3c (=60, stub length convention: total-24... actually the
               fixed template value; treat this as opaque and don't rebuild
               it from scratch — copy this ack template if implementing a
               server; a client only needs to read offsets 20-31)
offset 20-23   uint32 LE  assigned client/session id — echo this as
               `master_id` on the chat-bind handshake (below)
offset 24-31   DCE/RPC sec_addr: 2-byte length prefix (string length
               including NUL terminator) + NUL-terminated ASCII port string,
               zero-padded to fill this 8-byte slot. E.g. port 2000:
               05 00 '2' '0' '0' '0' 00 00
offset 32+     fixed template bytes (interface UUID etc.)
```

A handshake that fails validation gets a `0x0d` Nak (16 bytes: header only,
`offset 8-15` = length `0x10` + zero sequence-adjacent bytes) and the
connection is then closed by the server.

### Chat-bind handshake (exactly 72 bytes declared)

Self-consistent, fixed-shape, byte-exact except for `seq` and `masterId`:

```
05 00 0b 03 10 00 00 00 48 00 00 00 <seq:4>    d0 16 d0 16
<masterId:4>                        01 00 00 00 01 00 01 00
a2 a0 6e d7 f0 2c b9 46 a8 7b e4 22 77 bc 1e f2   <- ChatInterfaceGuid
01 00 00 00 04 5d 88 8a eb 1c c9 11 9f e8 08 00
2b 10 48 60 02 00 00 00
```

- `masterId` (offset 20-23) = the client/session id returned by one of the
  two position handshakes above — this binds the chat connection to that
  already-registered player identity.
- `ChatInterfaceGuid` at offset 32-47 = `a2 a0 6e d7 f0 2c b9 46 a8 7b e4 22
  77 bc 1e f2`, fixed. (Some real clients send the position `InterfaceGuid`
  here instead; this server accepts either. A conforming new client should
  send `ChatInterfaceGuid` as documented.)

Response: same 60-byte Ack shape as the position handshake, echoing the
resolved client's id at offset 20-23. If `masterId` doesn't resolve to a
known client, the server drops the connection without sending any response
at all (no Nak — the request was well-formed, the referenced id just doesn't
exist).

**Which shape a given `0x0b` request is: decided purely by declared length.**
Exactly 72 → chat-bind. Anything else → position/primary handshake.

## Mandatory post-handshake packet (`0x0e` → `0x0f`, position connections only)

Send this once, right after your position handshake completes (before your
first position update). The server acks unconditionally and never inspects
its payload — payload shape/content doesn't matter for interop with this
server, but for reference, real clients send a fixed 56-ish byte packet
whose payload includes the same `ChatInterfaceGuid` noted above. Response is
a fixed 56-byte template with the client id patched at offset 20-23,
sequence echoed as usual.

This step is **not required on the chat connection** — only on position
connections.

## Data packets (`0x00`, C→S) — common prefix

Every data request shares a 48-byte prefix:

```
offset 0-15    standard 16-byte header
offset 16-19   uint32 LE = totalLength - 24   (redundant "stub length")
offset 20-21   uint16 LE  major
offset 22-23   uint16 LE  minor
offset 24-43   password, ASCII, NUL-padded, 20 bytes — selects which "domain"
               (room) this connection belongs to; any two clients using the
               same password see each other
offset 44-47   4 bytes — client's local IPv4 address (sent, not validated
               server-side)
```

Operations, by `(major, minor)`:

| major | minor | Operation | Connection |
|---|---|---|---|
| 0 | 0, 1, or 3 | Position update | position |
| 0 | 2 | Query list | position |
| 1 | 0 | Chat send (or chat "registration", see below) | chat |
| 1 | 1 | Chat poll | chat |
| 2 | 0 | Extended: capability announcement | position |
| 2 | 1 | Extended: extended position update | position |
| 2 | 3 | Extended: extended-data poll | position |

Position update's minor field is not a stable discriminator — real clients
send `0`, `1`, and `3` interchangeably for byte-identical position payloads.
A client implementation can pick any of the three; `1` matches the original
stock-client capture and is the safest default.

Any `(major, minor)` combination not in this table is silently dropped by
the server (connection stays open, no response sent) — do not rely on
getting any reply for an unrecognized operation.

### Chat-family requests on a position connection

A real reference hub sends back an RPC [Fault](#faults) for a chat-family
(`major=1`) request arriving on a position connection, and treats that as
fatal to the connection client-side. **This server does not fault this
case** — under real concurrent multi-user load, real clients frequently
send chat polls on their own position connection by mistake (an apparent
client-side RPC binding-handle mixup), and faulting them caused constant
disconnect churn. This server dispatches chat-family requests correctly
regardless of which of a player's connections they arrive on. A new client
implementation should still route chat traffic to the dedicated chat
connection as documented above, both for interop with a real reference hub
and because that's the documented/intended shape of the protocol.

## Position update (140 bytes total)

48-byte common prefix, then a 92-byte block:

```
offset 48-111   name, ASCII, NUL-padded, 64 bytes
offset 112-123  reserved, 12 bytes — always zero; relayed verbatim
offset 124-139  position, 16 bytes — opaque to the server; relayed verbatim
                in query-list responses (a client is free to put its own
                coordinate encoding here, since the server never interprets it)
```

Response: a plain 28-byte [Ack](#response-discriminator) with `marker20=0`.

Sending this (or an [extended position update](#extended-position-update-2-1))
also marks the client as "present" for query-list purposes — see
[staleness](#query-list-presence).

## Query list (48 bytes total — common prefix only, no extra payload)

Request: `major=0, minor=2`, common 48-byte prefix, nothing else.

Response:

```
offset 0-23    standard response header (offset 16-19 = length - 24)
offset 24-27   uint32 LE  player count
offset 28-31   uint32 LE  = 1 (constant)
offset 32-35   uint32 LE  player count (duplicated)
offset 36..    N x 92-byte player blocks (name[64] + reserved[12] + position[16],
               same layout as the position-update payload above)
offset (36+92N)..+4   4 zero bytes (trailer)
```

`marker20` (offset 20-23 of the response) is `0`; length is `36 + 92N + 4`,
which discriminates it from the other response shapes (see
[Response discriminator](#response-discriminator)).

### Query-list presence (staleness, not connection-based)

There is no logout packet in this protocol. A client's own "disconnect" UI
action typically leaves its TCP connections open and simply stops sending
position updates. This server includes a client in query-list output only
if it received a position update (standard or extended) within the last
**5 seconds** — a client that goes quiet longer than that vanishes from
other players' lists even though its sockets may still be open. A client
implementation should therefore send position updates at an interval
comfortably under 5 seconds to stay visible.

Query-list results are **not deduplicated by name** — because a real player
has two independent position connections, a query-list response may
legitimately contain two blocks with the same name if both legs are within
the staleness window. Don't treat that as a bug to filter client-side; it
reflects the two-position-connection topology above.

## Chat poll (C→S, chat connection only)

Request: 48-byte common prefix + 4-byte constant `00 00 02 00` + player name
as an [NDR-ish string](#ndr-ish-strings) (unpadded — it's the last field in
the packet).

Response, one of two shapes:

**NoChat** (nothing pending) — fixed 44 bytes:

```
offset 0-15    standard header
offset 16-19   0x14 (stub-length convention: 44-24=20=0x14)
offset 20-23   marker20 = 1
offset 24-43   zero-filled (content beyond this point unconfirmed against
               a live capture; this server sends all zeros here)
```

**ChatData** (message pending) — 24-byte header (`marker20=1`, length =
actual size) followed by the same opaque body a chat-send request produced
(see below) — dequeue and forward one queued message per poll.

Poll frequently (real clients poll on a tight interval, sub-second) — this
is the only delivery mechanism for both chat text and font/color data;
there's no push/notify.

## Chat send (C→S, chat connection only)

Request: 48-byte common prefix + 4 zero bytes + `uint32 subtype` + 4-byte
constant `00 00 02 00` + NDR sender name (**padded** to a 4-byte boundary) +
a subtype-specific trailing field.

```
offset 0-47    common prefix
offset 48-51   4 zero bytes
offset 52-55   uint32 LE  subtype (1 = chat text, 2 = registration, 3 = font/color)
offset 56-59   4-byte constant 00 00 02 00
offset 60..    NDR sender name, padded to 4-byte boundary
...            subtype-specific trailing field, see below
```

Response: 28-byte [Ack](#response-discriminator) with `marker20=1`
(chat-family), regardless of subtype.

### Subtype 1 — chat text

Trailing field: NDR-ish string holding the message text, using the `offset`
field of the NDR triple set to the special marker `0x00020004` instead of
`0`. This is the last field in the request, so the sender does **not** pad
it — but the server pads it when relaying (see below).

### Subtype 2 — registration

Historically sent once by a client, right after its first position update,
**on the position connection** (where — per the connection-topology note
above — a real reference hub faults it; this server accepts it but drops
it silently). It is not a chat message. Payload: 4 zero bytes + subtype(4)=2
+ `0x00020000` marker(4) + player name as an unpadded NDR-ish string + a
10-byte zero trailer. A conforming new client does not need to send this at
all — this server does not require it, and it is never relayed to peers even
if received on the chat connection.

### Subtype 3 — font/color

Same header as subtype 1 (prefix + 4 zero bytes + subtype(4)=3 +
`0x00020000` marker(4) + NDR sender name), then a fixed-shape trailing
field instead of an NDR text string:

```
+12 bytes  NDR conformant-array header wrapping the blob below
           (maxCount, marker=0x00020004, actualCount — all three set to the
           blob's byte length, 64)
+4 bytes   COLORREF — 0x00BBGGRR, the player's name-label text color
+60 bytes  LOGFONTA (Win32 struct, verbatim: lfHeight, lfWidth, lfEscapement,
           lfOrientation, lfWeight, lfItalic, lfUnderline, lfStrikeOut,
           lfCharSet, lfOutPrecision, lfClipPrecision, lfQuality,
           lfPitchAndFamily, lfFaceName[32]) — exactly sizeof(LOGFONTA)=0x3c.
           `lfHeight` follows the standard LOGFONT convention:
           lfHeight = -(pointSize * 96 / 72).
```

A receiving client locates this 64-byte blob (COLORREF+LOGFONTA) from **the
end of the packet**, not via any internal length prefix — i.e. at
`declaredLength - 64` (after accounting for the trailing padding described
next). Get this wrong and font delivery breaks even though chat text
delivery (subtype 1, self-describing NDR string) tolerates it.

### Relay padding (server behavior, confirmed live 2026-08-10)

When relaying a subtype-1 or subtype-3 chat-send packet to recipients (via
chat poll), this server pads the trailing field (text for subtype 1,
COLORREF+LOGFONTA blob for subtype 3) up to a 4-byte boundary and appends
one extra all-zero trailing `uint32`, regardless of that field's own
length/alignment — this matches confirmed real-hub relay behavior. A
sender's own outgoing packet does **not** include this padding; it's added
purely on relay. A client implementation only needs to know this when
**parsing a received ChatData response** — expect up to 3 bytes of zero
padding plus one full zero dword after the text/font field, and locate the
font blob from the end of the packet accordingly (i.e. `declaredLength - 4
- pad - 64`, or more robustly, scan for the actual_count field and compute
from there rather than assuming no trailer).

## NDR-ish strings

Used for player/sender names and chat text throughout:

```
uint32 LE  max_count      (chars including NUL)
uint32 LE  offset         (0, except the chat-text field: 0x00020004)
uint32 LE  actual_count   (same as max_count)
<max_count> bytes         ASCII chars + NUL terminator
```

Padding: pad to the next 4-byte boundary if another field follows it in the
same packet on the wire; the last field in a request is left unpadded by
the sender (see the relay-padding note above for what a *response* looks
like after the server pads it).

## Response discriminator (type `0x02` responses)

The "stub length" field (offset 16-19) is always `total_length - 24` and
can never be used to distinguish response subtypes. Use declared total
length first, then `marker20` (offset 20-23) only where needed:

| length | marker20 | Meaning |
|---|---|---|
| 28 | 0 | Ack (position-family) |
| 28 | 1 | Ack (chat-family) |
| 44 | 1 | NoChat |
| anything else | 0 | PlayerList, or extended-poll response with an empty/absent body |
| anything else | 1 | ChatData |
| anything else | 2 | Extended-poll response carrying an extended-position body (see below) |

## Faults (`0x03`, S→C)

Fixed 32-byte shape:

```
05 00 03 03 10 00 00 00 20 00 00 00 <seq:4>
20 00 00 00 00 00 00 00 0b 00 01 1c 00 00 00 00
```

Offset 24-27 is an NCA-style status code (the one confirmed live value,
`0x1c01000b`, decodes as "Win32 error mapped into NCA status space" +
`ERROR_BAD_FORMAT`). This server currently never sends a Fault (the
connection-role gate that used to trigger it was removed — see above); a
real reference hub sends one for chat-family requests on the wrong
connection, and reportedly also for reconnecting under the same player name
too soon after a prior session (exact cooldown unconfirmed, not implemented
by this server). A client should treat receipt of any `0x03` packet as
fatal to that connection and reconnect/re-handshake.

## Extended protocol overlay (ClassicUO)

Opt-in overlay on top of the stock protocol, `major=2`, all on the
**position** connection. A stock UOAM/uoam_Orion client never sends these
and is unaffected by their existence — extended data is only ever relayed
to other clients that have themselves announced support for it, so it's
fully invisible to stock clients sharing the same domain (password).

Use this if you're writing a richer client (e.g. a ClassicUO-based one) that
wants to share serial id, vitals, facet, guild/notoriety, velocity, and
status flags with other extended-capable clients in the same domain, in
addition to (not instead of) the standard position feed that keeps you
visible to stock clients.

### `(2, 0)` — capability announcement

Send once, right after the mandatory post-handshake (`0x0e`/`0x0f`)
exchange — i.e. at the point a stock client would send its first position
update.

```
48-byte common prefix
offset 48-51   ASCII magic "CUOX" (0x43 0x55 0x4F 0x58)
offset 52-53   uint16 LE  protocol version (currently 1)
```

Response: plain 28-byte Ack (`marker20=0`), same as a position update's ack.
Any version value is accepted (no negotiation/rejection in this version of
the server) — sending an unrecognized version still marks you as
extended-capable at minimum.

### `(2, 1)` — extended position update

Send on every tick **in addition to** the standard `(0, *)` position
update, not instead of it.

```
48-byte common prefix
offset 48-51   uint32 LE   SerialId
offset 52-53   uint16 LE   Hp
offset 54-55   uint16 LE   MaxHp
offset 56-57   uint16 LE   Mana
offset 58-59   uint16 LE   MaxMana
offset 60-61   uint16 LE   Stamina
offset 62-63   uint16 LE   MaxStamina
offset 64      byte        Facet (0=Felucca, 1=Trammel, 2=Ilshenar, 3=Malas, 4=Tokuno, 5=TerMur)
offset 65      byte        Notoriety (UO client convention: 1=innocent, 2=friend,
                            3=grey/attackable, 4=criminal, 5=enemy, 6=murderer, 7=invulnerable)
offset 66-69   uint32 LE   GuildId (0 = none; opaque, not interpreted server-side)
offset 70-71   uint16 LE   Flags (bitfield, LSB=bit0: bit0=mounted, bit1=hidden/stealthed,
                            bit2=war mode, bit3=poisoned, bit4=paralyzed, bits 5-15 reserved — send 0)
offset 72-73   int16 LE    VelocityX (signed, tiles/sec * 100)
offset 74-75   int16 LE    VelocityY (signed, same scale)
offset 76-83   uint64 LE   Timestamp (client-side Unix epoch millis; not validated server-side)
```

Total request length: 48 + 36 = 84 bytes. Response: plain 28-byte Ack
(`marker20=0`), same as standard position update. This also counts as
presence for query-list staleness purposes, same as a standard position
update.

Server behavior: stores the 36-byte body verbatim per sender, then enqueues
a copy for every other client in the same domain that has itself announced
`(2,0)` support — filtered exactly like chat, but on an independent queue
(bounded, currently 64 entries per recipient; oldest-unconsumed entries are
dropped, not the newest, once the queue is full — poll frequently). Never
delivered to stock (non-extended) clients.

### `(2, 3)` — extended-data poll

Request: `major=2, minor=3`, common 48-byte prefix only, no extra payload.

Response:

```
offset 0-15    standard header
offset 16-19   0 (unused for this response)
offset 20-23   marker20 = 2   (extended-family discriminator)
offset 24..    present only if a queued update was available: the 36-byte
               body from a peer's (2,1) request, verbatim (SerialId..Timestamp,
               same layout as the request above)
```

If nothing is queued, the response is the 24-byte header alone (`marker20=2`,
length 24). Poll this on a tight interval, same as chat poll — this is the
only delivery mechanism for extended data, there's no push.

### `(2, 2)` — reserved

Not implemented by this server. Reserved for a future map-markers/
annotations feature; do not use this minor value for anything else.

### Dedup is client-side

`SerialId` in the extended payload is the same Ultima Online serial a
ClassicUO-based client already has for any entity visible via its own game
connection. The server does not attempt any deduplication — it just relays
`SerialId` faithfully. A client that already renders an entity live (via its
own game connection) should suppress the redundant marker for any extended
update carrying that same `SerialId`.

## Practical notes for a client implementation

- Open all three connections (two position, one chat) at session start;
  a single-connection client can send position updates and poll the query
  list, but cannot use chat and will fault on a strict reference hub for
  trying (though not on this server).
- Send position updates at least every few seconds (well under the 5-second
  staleness window) to stay visible to other clients' query lists.
- Poll chat (and, if using the extended overlay, extended data) frequently
  — sub-second — since both are pull-only with no server push.
- Treat any `0x03` Fault as fatal to that connection: reconnect and redo the
  handshake for that leg.
- The server does not send an application-level logout/disconnect
  notification; presence is inferred purely from position-update staleness.
