using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Md2Pdf.Core;

public static class ManagedBrowserManager
{
    private const long MaxArchiveBytes = 300L * 1024 * 1024;
    private const long MaxExpandedBytes = 700L * 1024 * 1024;
    private const int MaxEntries = 10_000;

    public static BrowserManifest Manifest { get; } = LoadManifest();

    public static string? GetInstalledExecutablePath()
    {
        var platform = CurrentPlatform();
        if (platform is null) return null;
        var path = Path.Combine(
            ProductPaths.ManagedBrowserRoot,
            Manifest.Version,
            platform.Executable.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? path : null;
    }

    public static BrowserManagementResult Status()
    {
        var path = GetInstalledExecutablePath();
        return new BrowserManagementResult
        {
            Action = "status",
            Version = Manifest.Version,
            Path = path,
            Installed = path is not null,
            Success = true,
            ExitCode = ExitCodes.Success
        };
    }

    public static async Task<BrowserManagementResult> InstallAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new BrowserManagementResult
        {
            Action = "install",
            Version = Manifest.Version,
            ExitCode = ExitCodes.InstallationFailure
        };
        var platform = CurrentPlatform();
        if (platform is null)
        {
            result.Errors.Add("Managed browser installation supports only win-x64 and linux-x64.");
            return result;
        }
        if (!IsSha256(platform.Sha256))
        {
            result.Errors.Add("The embedded browser manifest has no valid SHA-256.");
            return result;
        }

        var installed = GetInstalledExecutablePath();
        if (installed is not null)
        {
            var version = await BrowserRunner.ProbeVersionAsync(installed, cancellationToken)
                .ConfigureAwait(false);
            if (version?.Contains(Manifest.Version, StringComparison.Ordinal) == true &&
                await ProbePrintAsync(installed, cancellationToken).ConfigureAwait(false))
            {
                result.Success = true;
                result.Installed = true;
                result.Path = installed;
                result.ExitCode = ExitCodes.Success;
                return result;
            }
            result.Warnings.Add("The existing managed browser is corrupt or has the wrong version.");
        }

        var root = Path.GetFullPath(ProductPaths.ManagedBrowserRoot);
        Directory.CreateDirectory(root);
        var staging = Path.Combine(root, ".stage-" + Guid.NewGuid().ToString("N"));
        var archive = Path.Combine(staging, "browser.zip");
        var payload = Path.Combine(staging, "payload");
        var destination = Path.Combine(root, Manifest.Version);
        var backup = destination + ".backup-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(payload);

        try
        {
            await DownloadAsync(platform.Url, archive, cancellationToken).ConfigureAwait(false);
            var hash = await HashAsync(archive, cancellationToken).ConfigureAwait(false);
            if (!hash.Equals(platform.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Browser archive SHA-256 mismatch. Expected {platform.Sha256}, got {hash}.");

            ExtractSafely(archive, payload);
            var executable = Path.Combine(
                payload,
                platform.Executable.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(executable))
                throw new InvalidDataException(
                    $"Browser archive does not contain {platform.Executable}.");

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    executable,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }

            var version = await BrowserRunner.ProbeVersionAsync(executable, cancellationToken)
                .ConfigureAwait(false);
            if (version?.Contains(Manifest.Version, StringComparison.Ordinal) != true)
                throw new InvalidDataException(
                    "Downloaded browser did not pass its version probe.");
            if (!await ProbePrintAsync(executable, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException(
                    "Downloaded browser did not pass its sandboxed PDF print probe.");

            if (Directory.Exists(destination)) Directory.Move(destination, backup);
            try
            {
                Directory.Move(payload, destination);
                if (Directory.Exists(backup)) Directory.Delete(backup, recursive: true);
            }
            catch
            {
                if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
                if (Directory.Exists(backup)) Directory.Move(backup, destination);
                throw;
            }

            result.Path = Path.Combine(
                destination,
                platform.Executable.Replace('/', Path.DirectorySeparatorChar));
            result.Installed = true;
            result.Success = true;
            result.ExitCode = ExitCodes.Success;
            return result;
        }
        catch (Exception exception) when (exception is HttpRequestException
                                               or IOException
                                               or UnauthorizedAccessException
                                               or InvalidDataException
                                               or JsonException)
        {
            result.Errors.Add(exception.Message);
            return result;
        }
        finally
        {
            TryDeleteDirectory(staging);
            if (Directory.Exists(backup) && Directory.Exists(destination))
                TryDeleteDirectory(backup);
        }
    }

    public static BrowserManagementResult Remove()
    {
        var result = new BrowserManagementResult
        {
            Action = "remove",
            Version = Manifest.Version,
            ExitCode = ExitCodes.InstallationFailure
        };
        var destination = Path.GetFullPath(Path.Combine(
            ProductPaths.ManagedBrowserRoot, Manifest.Version));
        var root = Path.GetFullPath(ProductPaths.ManagedBrowserRoot)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(root, PathComparison()))
        {
            result.Errors.Add("Managed browser path escaped its owned cache root.");
            return result;
        }

        try
        {
            if (Directory.Exists(destination))
                Directory.Delete(destination, recursive: true);
            result.Success = true;
            result.Installed = false;
            result.ExitCode = ExitCodes.Success;
            return result;
        }
        catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException)
        {
            result.Errors.Add(exception.Message);
            return result;
        }
    }

    private static BrowserManifestPlatform? CurrentPlatform()
    {
        var rid = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "win-x64" : ""
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) &&
              RuntimeInformation.ProcessArchitecture == Architecture.X64
                ? "linux-x64"
                : "";
        return Manifest.Platforms.FirstOrDefault(item =>
            item.Rid.Equals(rid, StringComparison.Ordinal));
    }

    private static BrowserManifest LoadManifest()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Md2Pdf.Core.browser-manifest.json")
            ?? throw new InvalidOperationException("Embedded browser manifest is missing.");
        return JsonSerializer.Deserialize(stream, Md2PdfJsonContext.Default.BrowserManifest)
            ?? throw new InvalidOperationException("Embedded browser manifest is invalid.");
    }

    private static async Task DownloadAsync(
        string url,
        string destination,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("md2pdf/0.1");
        using var response = await client.GetAsync(
            url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxArchiveBytes)
            throw new InvalidDataException("Browser archive exceeds the compressed size limit.");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var output = new FileStream(
            destination, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 1024 * 1024, useAsync: true);
        var buffer = new byte[1024 * 1024];
        long total = 0;
        while (true)
        {
            var count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            total += count;
            if (total > MaxArchiveBytes)
                throw new InvalidDataException("Browser archive exceeds the compressed size limit.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<string> HashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1024 * 1024, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static async Task<bool> ProbePrintAsync(
        string executable,
        CancellationToken cancellationToken)
    {
        var probeRoot = Path.Combine(
            Path.GetTempPath(), "md2pdf-browser-probe-" + Path.GetRandomFileName());
        Directory.CreateDirectory(probeRoot);
        var html = Path.Combine(probeRoot, "probe.html");
        var pdf = Path.Combine(probeRoot, "probe.pdf");
        try
        {
            await File.WriteAllTextAsync(
                html,
                "<!doctype html><html><body><h1>MD2PDF browser probe</h1><p>" +
                new string('x', 2048) + "</p></body></html>",
                cancellationToken).ConfigureAwait(false);
            var result = await BrowserRunner.PrintAsync(
                executable,
                html,
                pdf,
                TimeSpan.FromSeconds(30),
                cancellationToken).ConfigureAwait(false);
            return result.Success && PdfValidation.IsUsable(pdf, out _);
        }
        finally
        {
            TryDeleteDirectory(probeRoot);
        }
    }

    internal static void ExtractSafely(string archivePath, string destination)
    {
        var root = Path.GetFullPath(destination)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaxEntries)
            throw new InvalidDataException("Browser archive contains too many entries.");

        long expanded = 0;
        foreach (var entry in archive.Entries)
        {
            expanded += entry.Length;
            if (expanded > MaxExpandedBytes)
                throw new InvalidDataException("Browser archive exceeds the expanded size limit.");

            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType == 0xA000)
                throw new InvalidDataException("Browser archive contains a symbolic link.");

            var target = Path.GetFullPath(Path.Combine(
                destination,
                entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(root, PathComparison()))
                throw new InvalidDataException("Browser archive contains a path traversal entry.");

            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: false);
            if (!OperatingSystem.IsWindows())
            {
                var mode = (UnixFileMode)((entry.ExternalAttributes >> 16) & 0x1FF);
                if (mode != 0) File.SetUnixFileMode(target, mode);
            }
        }
    }

    internal static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static StringComparison PathComparison() =>
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException)
        {
            // Staging cleanup is best effort and never broadens beyond the generated path.
        }
    }
}
