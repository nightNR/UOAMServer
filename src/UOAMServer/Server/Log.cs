namespace UOAMServer.Server;

// Robustness (design spec) — a minimal, fixed-format diagnostic logger, not
// the "logging framework" the design spec explicitly defers as future work.
// No sinks, no configuration, just a runtime-settable floor so --verbose can
// turn on Debug-level connection/packet tracing without a framework.
public static class Log
{
    public static bool DebugEnabled { get; set; }

    public static void Debug(string message)
    {
        if (DebugEnabled)
            Write("DEBUG", message);
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    private static void Write(string level, string message) =>
        Console.Error.WriteLine($"{DateTime.UtcNow:O} [{level}] {message}");
}
