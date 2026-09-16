namespace Devlog.Core.Ai;

/// <summary>
/// A continuous-refill token bucket, not a fixed per-minute counter: an
/// interactive Ask can spend its rounds immediately while the bucket is
/// full, and a multi-batch narrate is throttled only once it has actually
/// drained the bucket — not held to an artificial "one request per
/// (60/capacity) seconds" cadence that would slow down the common case to
/// protect against the rare one.
/// <para>
/// Pure logic, no I/O, no ambient clock — the clock is injected so tests can
/// drive time without a real <c>Task.Delay</c>. Not thread-safe on its own;
/// the caller (<c>ChatClassifier</c>) serializes access with a semaphore.
/// </para>
/// </summary>
public sealed class TokenBucket
{
    private readonly int _capacity;
    private readonly double _tokensPerSecond;
    private readonly Func<DateTimeOffset> _clock;
    private double _tokens;
    private DateTimeOffset _lastRefill;

    public TokenBucket(int capacity, TimeSpan refillPeriod, Func<DateTimeOffset>? clock = null)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (refillPeriod <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(refillPeriod));

        _capacity = capacity;
        _tokensPerSecond = capacity / refillPeriod.TotalSeconds;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _tokens = capacity;
        _lastRefill = _clock();
    }

    /// <summary>How long to wait before a token is available, refilling first. Zero means take it now.</summary>
    public TimeSpan TimeUntilNextToken()
    {
        Refill();
        if (_tokens >= 1)
        {
            return TimeSpan.Zero;
        }

        var deficit = 1 - _tokens;
        return TimeSpan.FromSeconds(deficit / _tokensPerSecond);
    }

    /// <summary>Spends one token. Callers should only do this after <see cref="TimeUntilNextToken"/> returned zero (or after waiting that long).</summary>
    public void Consume()
    {
        Refill();
        _tokens = Math.Max(0, _tokens - 1);
    }

    private void Refill()
    {
        var now = _clock();
        var elapsedSeconds = (now - _lastRefill).TotalSeconds;
        if (elapsedSeconds <= 0)
        {
            return;
        }

        _tokens = Math.Min(_capacity, _tokens + elapsedSeconds * _tokensPerSecond);
        _lastRefill = now;
    }
}
