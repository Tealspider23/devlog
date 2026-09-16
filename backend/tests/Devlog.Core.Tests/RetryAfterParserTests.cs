using Devlog.Core.Ai;
using Xunit;

namespace Devlog.Core.Tests;

public class RetryAfterParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ParsesIntegerSeconds()
    {
        var result = RetryAfterParser.Parse("30", Now);
        Assert.Equal(TimeSpan.FromSeconds(30), result);
    }

    [Fact]
    public void ParsesHttpDateInTheFuture()
    {
        var future = Now.AddSeconds(45);
        var result = RetryAfterParser.Parse(future.ToString("R"), Now);

        Assert.NotNull(result);
        Assert.InRange(result!.Value.TotalSeconds, 44, 46);
    }

    [Fact]
    public void HttpDateInThePast_ReturnsZero_NeverNegative()
    {
        var past = Now.AddSeconds(-60);
        var result = RetryAfterParser.Parse(past.ToString("R"), Now);

        Assert.Equal(TimeSpan.Zero, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-value")]
    public void MissingOrUnparseable_ReturnsNull(string? headerValue)
    {
        Assert.Null(RetryAfterParser.Parse(headerValue, Now));
    }

    [Fact]
    public void NegativeSeconds_ReturnsNull()
    {
        Assert.Null(RetryAfterParser.Parse("-5", Now));
    }
}
