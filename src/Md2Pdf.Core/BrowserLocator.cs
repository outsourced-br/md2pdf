using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Md2Pdf.Core;

public sealed record BrowserLocation(string Path, string Source);

public interface IBrowserDiscoveryView
{
    bool IsWindows { get; }
    string? BrowserOverride { get; }
    string? ManagedBrowserPath { get; }
    IEnumerable<BrowserLocation> SystemBrowserLocations { get; }
    bool FileExists(string path);
    string GetFullPath(string path);
}

public static class BrowserLocator
{
    private static readonly string[] WindowsExecutableNames =
    [
        "msedge.exe",
        "chrome.exe",
        "brave.exe",
        "chrome-headless-shell.exe"
    ];

    private static readonly string[] LinuxExecutableNames =
    [
        "google-chrome-stable",
        "google-chrome",
        "chromium",
        "chromium-browser",
        "microsoft-edge-stable",
        "microsoft-edge",
        "brave-browser",
        "chrome-headless-shell"
    ];

    private static readonly string[] LinuxKnownLocations =
    [
        "/usr/bin/google-chrome-stable",
        "/usr/bin/google-chrome",
        "/usr/bin/chromium",
        "/usr/bin/chromium-browser",
        "/usr/bin/microsoft-edge-stable",
        "/usr/bin/microsoft-edge",
        "/usr/bin/brave-browser",
        "/usr/local/bin/google-chrome",
        "/usr/local/bin/chromium",
        "/snap/bin/chromium"
    ];

    public static List<BrowserCandidate> FindCandidates(
        string? explicitPath = null,
        bool managedOnly = false) =>
        FindCandidates(
            new SystemBrowserDiscoveryView(),
            explicitPath,
            managedOnly);

    public static List<BrowserCandidate> FindCandidates(
        IBrowserDiscoveryView view,
        string? explicitPath = null,
        bool managedOnly = false)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return [Candidate(view.GetFullPath(explicitPath), "explicit")];

        if (managedOnly)
        {
            var managed = view.ManagedBrowserPath;
            return managed is null ? [] : [Candidate(managed, "managed")];
        }

        var environmentPath = view.BrowserOverride;
        if (!string.IsNullOrWhiteSpace(environmentPath))
            return [Candidate(view.GetFullPath(environmentPath), "environment")];

        var locations = view.SystemBrowserLocations.ToList();
        if (view.ManagedBrowserPath is not null)
            locations.Add(new BrowserLocation(view.ManagedBrowserPath, "managed"));
        var comparer = view.IsWindows
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var seen = new HashSet<string>(comparer);
        var candidates = new List<BrowserCandidate>();
        foreach (var location in locations)
        {
            string fullPath;
            try
            {
                fullPath = view.GetFullPath(location.Path);
            }
            catch (Exception exception) when (exception is ArgumentException
                                                   or NotSupportedException
                                                   or PathTooLongException)
            {
                continue;
            }
            if (!view.FileExists(fullPath)) continue;
            if (!seen.Add(fullPath)) continue;
            candidates.Add(Candidate(fullPath, location.Source));
        }
        return candidates;
    }

    private static BrowserCandidate Candidate(string path, string source)
    {
        var portablePath = path.Replace('\\', '/');
        var file = Path.GetFileNameWithoutExtension(portablePath).ToLowerInvariant();
        var name = file switch
        {
            "msedge" or "microsoft-edge" or "microsoft-edge-stable" => "Microsoft Edge",
            "chrome" or "google-chrome" or "google-chrome-stable" => "Google Chrome",
            "chromium" or "chromium-browser" => "Chromium",
            "brave" or "brave-browser" => "Brave",
            "chrome-headless-shell" => "Chrome Headless Shell",
            _ => Path.GetFileName(portablePath)
        };
        return new BrowserCandidate { Path = path, Source = source, Name = name };
    }

    [SupportedOSPlatform("windows")]
    private static List<BrowserLocation> FindWindowsBrowsers()
    {
        var paths = new List<BrowserLocation>();
        paths.AddRange(ReadWindowsAppPaths()
            .Select(path => new BrowserLocation(path, "windows-app-path")));

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        paths.AddRange(new[]
        {
            Path.Combine(programFilesX86, @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(programFiles, @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(programFiles, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(programFilesX86, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(local, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(programFiles, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
            Path.Combine(programFilesX86, @"BraveSoftware\Brave-Browser\Application\brave.exe"),
            Path.Combine(local, @"BraveSoftware\Brave-Browser\Application\brave.exe")
        }.Select(path => new BrowserLocation(path, "windows-known")));
        paths.AddRange(FindOnPath(WindowsExecutableNames)
            .Select(path => new BrowserLocation(path, "path")));
        return paths;
    }

    [SupportedOSPlatform("windows")]
    private static List<string> ReadWindowsAppPaths()
    {
        var paths = new List<string>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    foreach (var executable in WindowsExecutableNames)
                    {
                        using var key = baseKey.OpenSubKey(
                            $@"Software\Microsoft\Windows\CurrentVersion\App Paths\{executable}");
                        if (key?.GetValue(null) is string path && File.Exists(path))
                            paths.Add(path);
                    }
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException
                                                       or IOException
                                                       or System.Security.SecurityException)
                {
                    // Registry discovery is best effort; known locations and PATH remain.
                }
            }
        }
        return paths;
    }

    private static List<BrowserLocation> FindLinuxBrowsers()
    {
        var paths = FindOnPath(LinuxExecutableNames)
            .Select(path => new BrowserLocation(path, "path"))
            .ToList();
        paths.AddRange(LinuxKnownLocations
            .Select(path => new BrowserLocation(path, "linux-known")));
        return paths;
    }

    private static List<string> FindOnPath(IEnumerable<string> names)
    {
        var result = new List<string>();
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var directory in path.Split(Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in names)
            {
                try
                {
                    var candidate = Path.Combine(directory, name);
                    if (File.Exists(candidate)) result.Add(candidate);
                }
                catch (Exception exception) when (exception is ArgumentException
                                                       or NotSupportedException
                                                       or PathTooLongException)
                {
                    // Ignore malformed PATH segments.
                }
            }
        }
        return result;
    }

    private sealed class SystemBrowserDiscoveryView : IBrowserDiscoveryView
    {
        public bool IsWindows => OperatingSystem.IsWindows();
        public string? BrowserOverride =>
            Environment.GetEnvironmentVariable("MD2PDF_BROWSER");
        public string? ManagedBrowserPath =>
            ManagedBrowserManager.GetInstalledExecutablePath();
        public IEnumerable<BrowserLocation> SystemBrowserLocations =>
            OperatingSystem.IsWindows()
                ? FindWindowsBrowsers()
                : FindLinuxBrowsers();
        public bool FileExists(string path) => File.Exists(path);
        public string GetFullPath(string path) => Path.GetFullPath(path);
    }
}
