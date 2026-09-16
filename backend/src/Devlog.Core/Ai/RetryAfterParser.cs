using System.Globalization;

namespace Devlog.Core.Ai;

/// <summary>
/// Parses a raw <c>Retry-After</c> header value (RFC 9110 §10.2.3: either an
/// integer number of seconds, or an HTTP-date) into a wait duration. Takes
/// the raw string rather than an <c>HttpResponseMessage</c> so it stays pure
/// and testable with no HTTP object to construct — <c>ChatClassifier</c> is
/// the only caller that touches an actual response.
/// </summary>
public static class RetryAfterParser
{
    public static TimeSpan? Parse(string? headerValue, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return null;
        }

        if (int.TryParse(headerValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        if (DateTimeOffset.TryParse(headerValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var when))
        {
            var delta = when - now;
            return delta > TimeSpan.Zero ? delta : TimeSpan.Zero;
        }

        return null;
    }
}
