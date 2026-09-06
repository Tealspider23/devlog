using Devlog.Core.Capture;

namespace Devlog.Core.Tests;

public class TitleKeyTests
{
    [Theory]
    [InlineData("Inbox (14) - Gmail - Google Chrome", "Inbox (15) - Gmail - Google Chrome")]
    [InlineData("Chat (3) | Microsoft Teams", "Chat (12) | Microsoft Teams")]
    [InlineData("[2] Notifications - GitHub", "[7] Notifications - GitHub")]
    public void CounterChanges_AreNotTreatedAsChanges(string before, string after)
    {
        // The single biggest source of junk rows: an unread badge ticking over is
        // not the user doing anything.
        Assert.Equal(TitleKey.For("chrome", before), TitleKey.For("chrome", after));
    }

    [Fact]
    public void ModifiedMarker_IsPreserved()
    {
        // The dot separates "actively editing this file" from "has it open".
        // That is signal, and it toggles dozens of times a day, not hundreds.
        var clean = TitleKey.For("Code", "auth.cs - devlog - Visual Studio Code");
        var dirty = TitleKey.For("Code", "● auth.cs - devlog - Visual Studio Code");

        Assert.NotEqual(clean, dirty);
    }

    [Fact]
    public void DifferentFiles_AreDifferentKeys()
    {
        var a = TitleKey.For("Code", "auth.cs - devlog - Visual Studio Code");
        var b = TitleKey.For("Code", "EventStore.cs - devlog - Visual Studio Code");

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void SameTitle_DifferentProcess_IsDifferentKey()
    {
        Assert.NotEqual(TitleKey.For("Code", "README.md"), TitleKey.For("devenv", "README.md"));
    }

    [Fact]
    public void CollapsesWhitespaceLeftByStripping()
    {
        Assert.Equal(
            TitleKey.For("chrome", "Inbox (14)  -  Gmail"),
            TitleKey.For("chrome", "Inbox - Gmail"));
    }

    [Fact]
    public void HandlesNulls()
    {
        Assert.Equal(TitleKey.For(null, null), TitleKey.For(null, null));
        Assert.NotEqual(TitleKey.For(null, null), TitleKey.For("Code", null));
    }
}
