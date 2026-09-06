using Devlog.Core.Derivation;
using Devlog.Core.Domain;

namespace Devlog.Core.Tests;

public class NoiseFilterTests
{
    private static RawEvent Focus(string? process, string? title) => new()
    {
        TsUtc = 0,
        Kind = EventKind.FocusChange,
        ProcessName = process,
        WindowTitle = title,
        IdleSeconds = 0
    };

    private readonly NoiseFilter _filter = new();

    [Theory]
    [InlineData("ShellExperienceHost")]
    [InlineData("LockApp")]
    [InlineData("StartMenuExperienceHost")]
    [InlineData("ShellHost")]
    [InlineData("PickerHost")]
    public void ShellSurfaces_AreNoise(string process) =>
        Assert.True(_filter.IsNoise(Focus(process, "whatever")));

    [Fact]
    public void LaunchAppsTab_IsRealAttention_NotNoise()
    {
        // It reads like a shell process name and was planned as one, but the
        // captured rows show it is a Chrome tab — the company app launcher.
        // Dropping it would delete real time.
        Assert.False(_filter.IsNoise(Focus("chrome", "LaunchApps - Google Chrome")));
    }

    [Theory]
    [InlineData("launch.example.com wants to")]
    [InlineData("meet.google.com wants to use your microphone")]
    public void BrowserPermissionPrompts_AreNoise(string title)
    {
        // Chrome renames its own window while a permission dialog is up, and the
        // result reads as a distinct site — one pending identity per host asked.
        // It is a modal on top of a page, not a page.
        Assert.True(_filter.IsNoise(Focus("chrome", title)));
    }

    [Fact]
    public void CheckingLink_IsNoise() =>
        Assert.True(_filter.IsNoise(Focus("chrome", "Checking link...")));

    [Fact]
    public void OrdinaryPageMentioningWants_IsKept()
    {
        // The pattern is "\bwants to\b", so it must not swallow a real page whose
        // title happens to contain the word.
        Assert.False(_filter.IsNoise(Focus("chrome", "What every developer wants - Some Blog")));
    }

    [Fact]
    public void RealActivity_IsNeverNoise()
    {
        Assert.False(_filter.IsNoise(Focus("Code", "Classifier.cs - devlog - Visual Studio Code")));
        Assert.False(_filter.IsNoise(Focus("chrome", "Model Context Protocol - Google Chrome")));
    }

    [Theory]
    [InlineData(EventKind.Lock)]
    [InlineData(EventKind.CollectorStop)]
    [InlineData(EventKind.Suspend)]
    public void StructuralEvents_SurviveEvenWithNoisyFields(EventKind kind)
    {
        // These carry the timeline's boundaries. Dropping one lets a duration run
        // straight through a lock or a shutdown — the 9h44m regression.
        var e = new RawEvent
        {
            TsUtc = 0,
            Kind = kind,
            ProcessName = "LockApp",
            WindowTitle = "Windows Default Lock Screen",
            IdleSeconds = 0
        };

        Assert.False(_filter.IsNoise(e));
    }

    [Fact]
    public void Apply_RemovesNoiseSoNeighboursCanMergeAcrossTheHole()
    {
        var kept = _filter.Apply(
        [
            Focus("Code", "a.cs - devlog - Visual Studio Code"),
            Focus("ShellExperienceHost", "New notification"),
            Focus("Code", "a.cs - devlog - Visual Studio Code")
        ]);

        Assert.Equal(2, kept.Count);
        Assert.All(kept, e => Assert.Equal("Code", e.ProcessName));
    }
}
