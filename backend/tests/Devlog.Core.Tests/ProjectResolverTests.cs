using Devlog.Core.Configuration;
using Devlog.Core.Derivation;

namespace Devlog.Core.Tests;

public class ProjectResolverTests
{
    private static ProjectResolver Resolver(params (string Path, string Project)[] repos) =>
        new(repos.Select(r => new RepoConfig { Path = r.Path, Project = r.Project }));

    [Fact]
    public void PathUnderConfiguredRoot_ResolvesToItsProject()
    {
        var resolver = Resolver((@"C:\repos\myapp-api", "myapp-api"));

        Assert.Equal(
            "myapp-api",
            resolver.Resolve(@"C:\repos\myapp-api\src\Controllers\HomeController.cs"));
    }

    /// <summary>
    /// The reason this class exists: two clones of the same service, configured
    /// with the same project name, must combine - not because they share a
    /// folder name (they do not have to), but because devlog cannot otherwise
    /// tell window titles from two identically-named repos apart.
    /// </summary>
    [Fact]
    public void TwoDifferentRootsCanMapToOneProject()
    {
        var resolver = Resolver(
            (@"C:\repos\team-a\myapp-api", "myapp-api"),
            (@"C:\repos\team-b-fork\myapp-api", "myapp-api"));

        Assert.Equal("myapp-api", resolver.Resolve(@"C:\repos\team-a\myapp-api\file.cs"));
        Assert.Equal("myapp-api", resolver.Resolve(@"C:\repos\team-b-fork\myapp-api\file.cs"));
    }

    [Fact]
    public void PathUnderNoConfiguredRoot_ReturnsNull()
    {
        var resolver = Resolver((@"C:\repos\myapp-api", "myapp-api"));
        Assert.Null(resolver.Resolve(@"C:\repos\unrelated-project\file.cs"));
    }

    /// <summary>
    /// The specific bug this guards: naive prefix matching on "orderbook" would
    /// also match "orderbook-ui", a sibling repo with a shared prefix but a
    /// different project entirely.
    /// </summary>
    [Fact]
    public void SiblingRepoWithSharedPrefix_DoesNotFalseMatch()
    {
        var resolver = Resolver(
            (@"C:\repos\orderbook", "orderbook-core"),
            (@"C:\repos\orderbook-ui", "orderbook-ui"));

        Assert.Equal("orderbook-ui", resolver.Resolve(@"C:\repos\orderbook-ui\src\App.tsx"));
        Assert.Equal("orderbook-core", resolver.Resolve(@"C:\repos\orderbook\src\Program.cs"));
    }

    [Fact]
    public void ForwardAndBackwardSlashes_ResolveTheSame()
    {
        var resolver = Resolver((@"C:\repos\myapp", "myapp"));

        Assert.Equal("myapp", resolver.Resolve("C:/repos/myapp/src/file.cs"));
        Assert.Equal("myapp", resolver.Resolve(@"C:\repos\myapp\src\file.cs"));
    }

    [Fact]
    public void TrailingSeparatorOnConfiguredRoot_DoesNotMatter()
    {
        var resolver = Resolver((@"C:\repos\myapp\", "myapp"));
        Assert.Equal("myapp", resolver.Resolve(@"C:\repos\myapp\src\file.cs"));
    }

    [Fact]
    public void ExactRootPath_Resolves()
    {
        var resolver = Resolver((@"C:\repos\myapp", "myapp"));
        Assert.Equal("myapp", resolver.Resolve(@"C:\repos\myapp"));
    }

    [Fact]
    public void NullOrEmptyPath_ReturnsNull()
    {
        var resolver = Resolver((@"C:\repos\myapp", "myapp"));

        Assert.Null(resolver.Resolve(null));
        Assert.Null(resolver.Resolve(""));
    }

    [Fact]
    public void KnownProjects_AreDistinctAcrossMultipleRoots()
    {
        var resolver = Resolver(
            (@"C:\repos\a\myapp-api", "myapp-api"),
            (@"C:\repos\b\myapp-api", "myapp-api"),
            (@"C:\repos\myapp-ui", "myapp-ui"));

        Assert.Equal(2, resolver.KnownProjects.Count);
        Assert.Contains("myapp-api", resolver.KnownProjects);
        Assert.Contains("myapp-ui", resolver.KnownProjects);
    }
}
