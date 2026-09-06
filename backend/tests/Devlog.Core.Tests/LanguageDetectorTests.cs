using Devlog.Core.Derivation;

namespace Devlog.Core.Tests;

public class LanguageDetectorTests
{
    [Theory]
    [InlineData("src/Program.cs", "C#")]
    [InlineData("src/App.tsx", "TypeScript")]
    [InlineData("src/index.ts", "TypeScript")]
    [InlineData("script.py", "Python")]
    [InlineData("Main.java", "Java")]
    [InlineData("main.go", "Go")]
    [InlineData("lib.rs", "Rust")]
    [InlineData("query.sql", "SQL")]
    [InlineData("README.md", "Markdown")]
    public void KnownExtensions_MapToTheirLanguage(string path, string expected) =>
        Assert.Equal(expected, LanguageDetector.Detect(path));

    [Fact]
    public void UnknownExtension_ReturnsNull() =>
        Assert.Null(LanguageDetector.Detect("data.xyz123"));

    [Fact]
    public void NoExtension_ReturnsNull() =>
        Assert.Null(LanguageDetector.Detect("Dockerfile"));

    [Fact]
    public void ExtensionMatching_IsCaseInsensitive() =>
        Assert.Equal("C#", LanguageDetector.Detect("Program.CS"));

    [Fact]
    public void DetectAll_DedupesAndPreservesFirstAppearanceOrder()
    {
        var result = LanguageDetector.DetectAll(
        [
            "a.cs", "b.ts", "c.cs", "d.py", "e.unknown", "f.ts"
        ]);

        Assert.Equal(["C#", "TypeScript", "Python"], result);
    }

    [Fact]
    public void DetectAll_EmptyInput_ReturnsEmpty() =>
        Assert.Empty(LanguageDetector.DetectAll([]));
}
