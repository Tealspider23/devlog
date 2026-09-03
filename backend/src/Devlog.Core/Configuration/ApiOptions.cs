namespace Devlog.Core.Configuration;

/// <summary>
/// The loopback HTTP surface the UI talks to. Everything here governs a door
/// into the whole activity log, so every default is the conservative one.
/// </summary>
public sealed class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>
    /// Off does not stop the collector from binding a listener — see the note on
    /// <c>MapDevlogApi</c> — but it stops any route beyond <c>/health</c> from
    /// answering. The nearest thing to the tray's pause button for the API.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Bound to <c>127.0.0.1</c> only — never configurable to anything else.</summary>
    public int Port { get; set; } = 5111;

    /// <summary>
    /// The Vite dev server's origin, e.g. <c>http://localhost:5173</c>. Null or
    /// empty disables CORS entirely, which is the production posture once the
    /// built frontend is served from this same origin.
    /// </summary>
    public string? DevCorsOrigin { get; set; }
}
