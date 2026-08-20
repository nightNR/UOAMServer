# UOAMServer

A from-scratch C# reimplementation of `uoamhub`, the multiplayer hub server for
[UOAM](http://www.uoam.net/) (Ultima Online Auto Map). Lets multiple UOAM
clients — including the `uoam_Orion` fork — share live player positions and
chat over a shared "domain" (a password acts as the room key).

The wire protocol is a DCE/RPC-shaped custom binary format reverse-engineered
from real client captures. See [`docs/protocol.md`](docs/protocol.md) for the
full client-implementable protocol reference (framing, handshake, all
packet shapes, and the ClassicUO extended overlay).

## Building

Requires the .NET SDK matching `src/UOAMServer/UOAMServer.csproj` (net10.0).

```
dotnet build src/UOAMServer/UOAMServer.csproj
dotnet test tests/UOAMServer.Tests/UOAMServer.Tests.csproj
```

## Running

```
dotnet run --project src/UOAMServer -- [options]
```

or run the built executable directly:

```
src/UOAMServer/bin/Debug/net10.0/UOAMServer.exe [options]
```

### CLI options

| Flag | Default | Meaning |
|---|---|---|
| `--port <n>` | `2000` | TCP port to listen on. Server binds dual-stack ([::]) so both IPv4 and IPv6 ("localhost" resolving to `::1`) clients connect immediately. |
| `--password <file>` | none | Path to a file whose first line is the *only* password the server will accept for a new domain (room). Without this flag, any password creates a new domain on first use — anyone who knows (or guesses) a password can join that room. |
| `--verbose` | off | Repeatable. Once enables `INFO`/`WARN` (already on by default); twice (`--verbose --verbose`) enables `DEBUG`-level connection and packet tracing (accept/close events, full hex dump of every packet in and out). Use this when diagnosing a client that can't connect or a protocol mismatch. |
| `--quiet` | off | Suppresses `INFO`/`WARN` logging entirely. |

Example — run on a non-default port with debug packet tracing:

```
dotnet run --project src/UOAMServer -- --port 2001 --verbose --verbose
```

## Connecting a client

Point a UOAM/uoam_Orion client at the server's IP and port, e.g.:

```
uoam_Orion.exe -q -c <server-ip> -n <your-name> -p 2000 -pw <room-password>
```

Any two clients using the **same password** land in the same domain (room)
and see each other's position, appear in each other's player list, and can
chat with each other. Different passwords are fully isolated rooms — unless
`--password <file>` is used to lock the server to one specific password.

## Extended protocol (ClassicUO overlay)

The server also supports an opt-in extended protocol for a custom
ClassicUO-based client — richer per-player data (serial, vitals, facet,
guild/notoriety, velocity, flags) relayed only between clients that
announce support for it, with zero behavior change for stock UOAM/uoam_Orion
clients in the same domain. See [`docs/protocol.md`](docs/protocol.md#extended-protocol-overlay-classicuo)
for the wire format.

## Troubleshooting a "can't connect" / protocol issue

Run with `--verbose --verbose` and reproduce the issue — the log will show,
per connection: accept/close events with the reason (idle timeout, peer
closed, socket exception), every packet's type/length/sequence, and a full
hex dump of both the received bytes and the server's response. Compare
against the protocol reference doc to spot a mismatch.
