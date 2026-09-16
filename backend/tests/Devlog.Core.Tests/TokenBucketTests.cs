using Devlog.Core.Ai;
using Xunit;

namespace Devlog.Core.Tests;

public class TokenBucketTests
{
    [Fact]
    public void StartsFull_AllowsImmediateConsumptionUpToCapacity()
    {
        var bucket = new TokenBucket(5, TimeSpan.FromMinutes(1));

        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(TimeSpan.Zero, bucket.TimeUntilNextToken());
            bucket.Consume();
        }
    }

    [Fact]
    public void WhenDrained_TimeUntilNextTokenIsPositive()
    {
        var bucket = new TokenBucket(5, TimeSpan.FromMinutes(1));

        for (int i = 0; i < 5; i++)
        {
            bucket.Consume();
        }

        Assert.True(bucket.TimeUntilNextToken() > TimeSpan.Zero);
    }

    /// <summary>
    /// A deterministic clock, not Thread.Sleep — the whole point of injecting
    /// the clock is that this test runs in milliseconds regardless of the
    /// real refill period.
    /// </summary>
    [Fact]
    public void RefillsOverTime_AccordingToInjectedClock()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var bucket = new TokenBucket(5, TimeSpan.FromMinutes(1), () => now);

        for (int i = 0; i < 5; i++)
        {
            bucket.Consume();
        }

        Assert.True(bucket.TimeUntilNextToken() > TimeSpan.Zero);

        // Half the refill period at capacity 5 -> ~2.5 tokens back: two full
        // tokens can be taken immediately, and a third still has to wait for
        // the remaining 0.5 to refill (at 5 tokens/60s, ~6s).
        now = now.AddSeconds(30);
        Assert.Equal(TimeSpan.Zero, bucket.TimeUntilNextToken());
        bucket.Consume();
        Assert.Equal(TimeSpan.Zero, bucket.TimeUntilNextToken());
        bucket.Consume();
        Assert.True(bucket.TimeUntilNextToken() > TimeSpan.Zero);
    }

    [Fact]
    public void NeverExceedsCapacity_EvenAfterALongIdlePeriod()
    {
        var now = DateTimeOffset.UtcNow;
        var bucket = new TokenBucket(5, TimeSpan.FromMinutes(1), () => now);

        now = now.AddHours(1);

        // Still only 5 tokens available, not 300.
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(TimeSpan.Zero, bucket.TimeUntilNextToken());
            bucket.Consume();
        }

        Assert.True(bucket.TimeUntilNextToken() > TimeSpan.Zero);
    }

    [Fact]
    public void InvalidCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TokenBucket(0, TimeSpan.FromMinutes(1)));
    }
}
