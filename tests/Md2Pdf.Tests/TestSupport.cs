using System.Runtime.InteropServices;

namespace Md2Pdf.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
    public const string Name = "process environment";
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory(string? label = null)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "md2pdf-tests",
            (label is null ? "" : label + " ") + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException)
        {
            // Test cleanup is best effort.
        }
    }
}

internal static class FakeBrowser
{
    public static string Find()
    {
        var root = FindRepositoryRoot();
        var configuration = AppContext.BaseDirectory.Contains(
            $"{System.IO.Path.DirectorySeparatorChar}Release{System.IO.Path.DirectorySeparatorChar}",
            StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
        var executable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "md2pdf-fake-browser.exe"
            : "md2pdf-fake-browser";
        var path = System.IO.Path.Combine(
            root,
            "tests",
            "Md2Pdf.FakeBrowser",
            "bin",
            configuration,
            "net10.0",
            executable);
        if (!System.IO.File.Exists(path))
            throw new FileNotFoundException("Fake browser apphost was not built.", path);
        return path;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "Md2Pdf.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the MD2PDF repository root.");
    }
}
