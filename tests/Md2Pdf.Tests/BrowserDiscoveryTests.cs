using Md2Pdf.Core;

namespace Md2Pdf.Tests;

public sealed class BrowserDiscoveryTests
{
    [Fact]
    public void ExplicitPathWinsOverEveryOtherSource()
    {
        var view = new FakeDiscoveryView
        {
            BrowserOverride = @"C:\env\chrome.exe",
            ManagedBrowserPath = @"C:\managed\chrome-headless-shell.exe",
            SystemBrowserLocations =
            [
                new BrowserLocation(@"C:\system\msedge.exe", "windows-app-path")
            ]
        };

        var result = BrowserLocator.FindCandidates(
            view, @"C:\explicit\brave.exe", managedOnly: false);

        var candidate = Assert.Single(result);
        Assert.Equal("explicit", candidate.Source);
        Assert.Equal("Brave", candidate.Name);
    }

    [Fact]
    public void EnvironmentOverrideWinsOverSystemAndManaged()
    {
        var view = new FakeDiscoveryView
        {
            BrowserOverride = "/custom/chromium",
            ManagedBrowserPath = "/managed/chrome-headless-shell",
            SystemBrowserLocations =
            [
                new BrowserLocation("/usr/bin/google-chrome", "linux-known")
            ]
        };

        var candidate = Assert.Single(BrowserLocator.FindCandidates(view));

        Assert.Equal("environment", candidate.Source);
        Assert.Equal("/custom/chromium", candidate.Path);
    }

    [Fact]
    public void PreservesSystemOrderSourcesAndManagedFallback()
    {
        var view = new FakeDiscoveryView
        {
            ManagedBrowserPath = "/managed/chrome-headless-shell",
            SystemBrowserLocations =
            [
                new BrowserLocation("/apps/edge", "windows-app-path"),
                new BrowserLocation("/users/chrome", "windows-known"),
                new BrowserLocation("/path/chromium", "path"),
                new BrowserLocation("/missing/brave", "path")
            ]
        };
        view.Missing.Add("/missing/brave");

        var result = BrowserLocator.FindCandidates(view);

        Assert.Collection(
            result,
            item => Assert.Equal("windows-app-path", item.Source),
            item => Assert.Equal("windows-known", item.Source),
            item => Assert.Equal("path", item.Source),
            item => Assert.Equal("managed", item.Source));
    }

    [Fact]
    public void ManagedOnlyDoesNotFallBack()
    {
        var view = new FakeDiscoveryView
        {
            SystemBrowserLocations =
            [
                new BrowserLocation("/usr/bin/chromium", "linux-known")
            ]
        };

        Assert.Empty(BrowserLocator.FindCandidates(view, managedOnly: true));

        view.ManagedBrowserPath = "/managed/chrome-headless-shell";
        var candidate = Assert.Single(
            BrowserLocator.FindCandidates(view, managedOnly: true));
        Assert.Equal("managed", candidate.Source);
    }

    private sealed class FakeDiscoveryView : IBrowserDiscoveryView
    {
        public bool IsWindows { get; init; }
        public string? BrowserOverride { get; init; }
        public string? ManagedBrowserPath { get; set; }
        public IEnumerable<BrowserLocation> SystemBrowserLocations { get; init; } = [];
        public HashSet<string> Missing { get; } = new(StringComparer.Ordinal);
        public bool FileExists(string path) => !Missing.Contains(path);
        public string GetFullPath(string path) => path;
    }
}
