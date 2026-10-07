namespace PaxPanel.Sensors;

/// <summary>Bytes-per-second from a monotonically growing byte counter.</summary>
public sealed class RateMeter
{
    long? _last;
    DateTime _lastAt;

    public double? Update(long totalBytes, DateTime at)
    {
        double? rate = null;
        if (_last is long previous && at > _lastAt && totalBytes >= previous)
            rate = (totalBytes - previous) / (at - _lastAt).TotalSeconds;
        _last = totalBytes;
        _lastAt = at;
        return rate;
    }

    public void Reset() => _last = null;
}
