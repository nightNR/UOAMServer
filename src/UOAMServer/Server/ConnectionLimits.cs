using System.Net;

namespace UOAMServer.Server;

public sealed class ConnectionLimits
{
    // Robustness (design spec) — headroom above the documented topology (up
    // to 3 legitimate connections per real player: two position legs + one
    // chat), enough for a couple of players behind the same NAT/router
    // without being high enough to be a meaningful flood vector.
    public const int MaxPerAddress = 8;

    private readonly object _lock = new();
    private readonly Dictionary<IPAddress, int> _counts = new();

    public bool TryAcquire(IPAddress address)
    {
        lock (_lock)
        {
            var count = _counts.GetValueOrDefault(address);
            if (count >= MaxPerAddress)
                return false;

            _counts[address] = count + 1;
            return true;
        }
    }

    public void Release(IPAddress address)
    {
        lock (_lock)
        {
            if (!_counts.TryGetValue(address, out var count))
                return;

            if (count <= 1)
                _counts.Remove(address);
            else
                _counts[address] = count - 1;
        }
    }
}
