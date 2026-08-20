namespace UOAMServer;

public sealed class CliArgs
{
    public int Port { get; private set; } = 2000;
    public string? PasswordFile { get; private set; }
    public int Verbosity { get; private set; } = 1;

    /// <summary>
    /// Compatibility workaround, off by default: minimum spacing (ms) enforced between
    /// consecutive responses written to the same connection. A real hub is reached over the
    /// internet, so a client never sees two responses arrive closer together than that
    /// connection's own round-trip latency; a same-host or LAN test server can deliver them
    /// back-to-back at near-zero latency instead. That degenerate tight spacing reproducibly
    /// crashed a real uoam_Orion 8.2.0.1 client (access violation, consistent faulting offset)
    /// purely from two query-list responses landing closer together than any real connection
    /// would ever produce — never reproduced against a real remote hub, only local/LAN testing.
    /// This does not fix a protocol bug (the wire format was verified byte-identical to the
    /// real hub's) - it re-adds artificial latency so local testing behaves like a real
    /// connection instead of exposing that timing-sensitive client bug.
    /// </summary>
    public int ResponseDelayMs { get; private set; }

    public static CliArgs Parse(string[] args)
    {
        var result = new CliArgs();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port":
                    result.Port = int.Parse(args[++i]);
                    break;
                case "--password":
                    result.PasswordFile = args[++i];
                    break;
                case "--verbose":
                    result.Verbosity++;
                    break;
                case "--quiet":
                    result.Verbosity = 0;
                    break;
                case "--response-delay-ms":
                    result.ResponseDelayMs = int.Parse(args[++i]);
                    break;
            }
        }

        return result;
    }
}
