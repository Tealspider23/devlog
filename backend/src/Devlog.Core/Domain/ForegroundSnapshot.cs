namespace Devlog.Core.Domain;

/// <summary>
/// What the OS could tell us about the foreground at one instant.
/// This is a layer-0 <em>sample</em> — it is never stored. Only samples that
/// differ from the previous one become a stored <see cref="RawEvent"/>.
/// </summary>
/// <param name="ProcessName">
/// e.g. <c>Code</c>, <c>chrome</c>, <c>devenv</c>. Null when no window has focus.
/// </param>
/// <param name="WindowTitle">
/// Raw and unnormalized, exactly as the OS reported it. Normalization happens
/// at derivation time so the rules can be rewritten without re-collecting.
/// </param>
/// <param name="ExePath">
/// Best-effort. Null for elevated processes when we aren't elevated —
/// <c>Process.MainModule</c> throws Access Denied there. ProcessName still resolves.
/// </param>
/// <param name="IdleSeconds">
/// Seconds since the last keyboard or mouse input, machine-wide.
/// A <em>measurement</em>, never a derived idle flag: reading documentation and
/// being away from the desk look identical here, and separating them is a
/// derivation-time decision that must stay retunable.
/// </param>
public sealed record ForegroundSnapshot(
    string? ProcessName,
    string? WindowTitle,
    string? ExePath,
    int IdleSeconds)
{
    private string? _comparisonKey;

    /// <summary>
    /// Normalized identity used for transition detection — see <see cref="Capture.TitleKey"/>.
    /// <para>
    /// Deliberately excludes <see cref="IdleSeconds"/>: idle time changes on every
    /// sample and would otherwise make every sample look like a transition.
    /// Also strips window-title counters, so an incrementing unread badge is not
    /// mistaken for the user doing something.
    /// </para>
    /// </summary>
    public string ComparisonKey => _comparisonKey ??= Capture.TitleKey.For(ProcessName, WindowTitle);

    /// <summary>Has the thing being attended to actually changed?</summary>
    public bool IsSameContextAs(ForegroundSnapshot? other) =>
        other is not null
        && string.Equals(ComparisonKey, other.ComparisonKey, StringComparison.Ordinal);
}
