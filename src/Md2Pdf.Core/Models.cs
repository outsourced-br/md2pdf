using System.Text.Json.Serialization;

namespace Md2Pdf.Core;

public static class ExitCodes
{
    public const int Success = 0;
    public const int InternalFailure = 1;
    public const int Usage = 2;
    public const int BrowserUnavailable = 3;
    public const int RenderFailure = 4;
    public const int InstallationFailure = 5;
}

public enum PaperSize
{
    A4,
    Letter,
    Legal
}

public enum CollisionPolicy
{
    Fail,
    Counter
}

public sealed class ConvertOptions
{
    public required string Input { get; init; }
    public string? Output { get; init; }
    public PaperSize Paper { get; init; } = PaperSize.A4;
    public bool Landscape { get; init; }
    public bool KeepHtml { get; init; }
    public bool Force { get; init; }
    public CollisionPolicy Collision { get; init; } = CollisionPolicy.Fail;
    public string? BrowserPath { get; init; }
    public bool ManagedBrowserOnly { get; init; }
}

public sealed class BrowserInfo
{
    public required string Source { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string? Version { get; init; }
}

public sealed class ConversionResult
{
    public int SchemaVersion { get; init; } = 1;
    public bool Success { get; set; }
    public string? Input { get; set; }
    public string? Output { get; set; }
    public string? Html { get; set; }
    public BrowserInfo? Browser { get; set; }
    public long Bytes { get; set; }
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];

    [JsonIgnore]
    public int ExitCode { get; set; }
}

public sealed class BrowserCandidate
{
    public required string Path { get; init; }
    public required string Source { get; init; }
    public required string Name { get; init; }
    public string? Version { get; set; }
    public bool Usable { get; set; }
    public string? Diagnostic { get; set; }
}

public sealed class DoctorResult
{
    public int SchemaVersion { get; init; } = 1;
    public bool Success { get; set; }
    public string Version { get; set; } = "";
    public List<BrowserCandidate> Browsers { get; } = [];
    public BrowserInfo? SelectedBrowser { get; set; }
    public bool PrintProbeSucceeded { get; set; }
    public string ManagedBrowserRoot { get; set; } = "";
    public bool ManagedBrowserRootWritable { get; set; }
    public bool ManagedBrowserInstalled { get; set; }
    public string? ManagedBrowserPath { get; set; }
    public string LogDirectory { get; set; } = "";
    public bool LogDirectoryWritable { get; set; }
    public bool? ExplorerInstalled { get; set; }
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];

    [JsonIgnore]
    public int ExitCode { get; set; }
}

public sealed class BrowserManagementResult
{
    public int SchemaVersion { get; init; } = 1;
    public bool Success { get; set; }
    public string Action { get; set; } = "";
    public string Version { get; set; } = "";
    public string? Path { get; set; }
    public bool Installed { get; set; }
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];

    [JsonIgnore]
    public int ExitCode { get; set; }
}

public sealed class ExplorerManagementResult
{
    public int SchemaVersion { get; init; } = 1;
    public bool Success { get; set; }
    public string Action { get; set; } = "";
    public bool Installed { get; set; }
    public string? Command { get; set; }
    public List<string> Errors { get; } = [];

    [JsonIgnore]
    public int ExitCode { get; set; }
}

public sealed class BrowserManifest
{
    public required string Version { get; init; }
    public required List<BrowserManifestPlatform> Platforms { get; init; }
}

public sealed class BrowserManifestPlatform
{
    public required string Rid { get; init; }
    public required string Url { get; init; }
    public required string Sha256 { get; init; }
    public required string Executable { get; init; }
}
