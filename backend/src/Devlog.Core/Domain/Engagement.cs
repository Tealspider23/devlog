namespace Devlog.Core.Domain;

/// <summary>
/// How you were engaged during an activity.
/// <para>
/// <b>Not derived from idle time.</b> Phase 1 capture of a real documentation
/// session showed <c>idle_seconds = 0</c> throughout, because scrolling is input:
/// <c>GetLastInputInfo</c> cannot tell reading from typing. Classifying
/// producing-vs-consuming from idle would mark every hour of learning as coding
/// — the opposite of what the brag document needs.
/// </para>
/// <para>
/// So engagement comes from the activity's <see cref="ActivityCategory"/>, and
/// idle is used only for what it is genuinely good at: detecting absence.
/// </para>
/// </summary>
public enum Engagement
{
    /// <summary>Making something — editor, terminal, IDE.</summary>
    Producing = 0,

    /// <summary>Taking something in — docs, tutorials, chat.</summary>
    Consuming = 1,

    /// <summary>At the machine but not touching it beyond the idle threshold.</summary>
    Idle = 2,

    /// <summary>Locked or suspended. Not at the machine at all.</summary>
    Away = 3
}
