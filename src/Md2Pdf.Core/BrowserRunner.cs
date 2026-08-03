using System.Diagnostics;
using System.Text;

namespace Md2Pdf.Core;

public sealed record BrowserPrintResult(
    bool Success,
    int? ExitCode,
    string StandardOutput,
    string StandardError,
    string? Error);

public static class BrowserRunner
{
    private const int CapturedCharacters = 64 * 1024;

    public static async Task<string?> ProbeVersionAsync(
        string browserPath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(browserPath)) return null;
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var fileVersion = FileVersionInfo.GetVersionInfo(browserPath);
                if (!string.IsNullOrWhiteSpace(fileVersion.ProductName) &&
                    IsChromiumProduct(fileVersion.ProductName) &&
                    NormalizeVersion(fileVersion.ProductVersion) is { } productVersion)
                    return productVersion;
            }
            catch (Exception exception) when (exception is IOException
                                                   or UnauthorizedAccessException)
            {
                // The process probe remains available.
            }
        }

        var start = new ProcessStartInfo
        {
            FileName = browserPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("--version");
        try
        {
            using var process = Process.Start(start);
            if (process is null) return null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var stdout = DrainBoundedAsync(process.StandardOutput, CancellationToken.None);
            var stderr = DrainBoundedAsync(process.StandardError, CancellationToken.None);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Stop(process);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested) throw;
                return null;
            }
            var output = (await stdout.ConfigureAwait(false)).Trim();
            var error = (await stderr.ConfigureAwait(false)).Trim();
            var versionText = output.Length > 0 ? output : error;
            return process.ExitCode == 0 ? NormalizeVersion(versionText) : null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
                                               or IOException
                                               or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static async Task<BrowserPrintResult> PrintAsync(
        string browserPath,
        string htmlPath,
        string pdfPath,
        TimeSpan? processTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var profile = Path.Combine(
            Path.GetTempPath(), "md2pdf-profile-" + Path.GetRandomFileName());
        Directory.CreateDirectory(profile);
        var start = new ProcessStartInfo
        {
            FileName = browserPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in new[]
        {
            "--headless",
            "--disable-gpu",
            "--disable-background-networking",
            "--disable-component-update",
            "--disable-default-apps",
            "--disable-extensions",
            "--disable-sync",
            "--metrics-recording-only",
            "--no-default-browser-check",
            "--no-first-run",
            "--no-pdf-header-footer",
            "--print-to-pdf-no-header",
            $"--user-data-dir={profile}",
            $"--print-to-pdf={pdfPath}",
            new Uri(Path.GetFullPath(htmlPath)).AbsoluteUri
        })
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            var timeoutDuration = processTimeout ?? TimeSpan.FromSeconds(120);
            using var process = Process.Start(start);
            if (process is null)
                return new BrowserPrintResult(false, null, "", "", "Browser process did not start.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(timeoutDuration);
            var stdoutTask = DrainBoundedAsync(process.StandardOutput, CancellationToken.None);
            var stderrTask = DrainBoundedAsync(process.StandardError, CancellationToken.None);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Stop(process);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested) throw;
                return new BrowserPrintResult(
                    false,
                    process.HasExited ? process.ExitCode : null,
                    await stdoutTask.ConfigureAwait(false),
                    await stderrTask.ConfigureAwait(false),
                    $"The browser did not exit within {timeoutDuration.TotalSeconds:0} seconds " +
                    "and was stopped.");
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            var success = process.ExitCode == 0;
            return new BrowserPrintResult(
                success,
                process.ExitCode,
                stdout,
                stderr,
                success ? null : $"The browser exited with code {process.ExitCode}.");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception
                                               or IOException
                                               or UnauthorizedAccessException)
        {
            return new BrowserPrintResult(false, null, "", "", exception.Message);
        }
        finally
        {
            TryDeleteDirectory(profile);
        }
    }

    private static async Task<string> DrainBoundedAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var retained = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            retained.Append(buffer, 0, count);
            if (retained.Length > CapturedCharacters)
                retained.Remove(0, retained.Length - CapturedCharacters);
        }
        return retained.ToString();
    }

    private static bool IsChromiumProduct(string productName) =>
        productName.Contains("Chrome", StringComparison.OrdinalIgnoreCase) ||
        productName.Contains("Chromium", StringComparison.OrdinalIgnoreCase) ||
        productName.Contains("Edge", StringComparison.OrdinalIgnoreCase) ||
        productName.Contains("Brave", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (var token in text.Split(
                     [' ', '\t', '\r', '\n', '/', '(', ')'],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = token.Trim().TrimEnd(',', ';');
            if (candidate.Length < 3 || !char.IsDigit(candidate[0])) continue;
            if (!candidate.Contains('.')) continue;
            if (candidate.All(character => char.IsDigit(character) ||
                                           character is '.' or '-' or '+'))
                return candidate;
        }
        return null;
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited between the state check and kill.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception exception) when (exception is IOException
                                                   or UnauthorizedAccessException)
            {
                if (attempt < 3) Thread.Sleep(100 * (attempt + 1));
            }
        }
    }
}
