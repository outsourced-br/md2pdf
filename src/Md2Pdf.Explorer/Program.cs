using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Md2Pdf.Explorer;

public static class Program
{
    private const uint MbIconError = 0x00000010;
    private const int MaxLogBytes = 1024 * 1024;
    private const int LogBackups = 4;
    private const int CapturedCharacters = 32 * 1024;

    public static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) return 5;
        var log = GetLogPath();
        if (args.Length != 1)
            return Fail(log, "Exactly one Markdown path is required.", "");

        var cli = Path.Combine(AppContext.BaseDirectory, "md2pdf.exe");
        if (!File.Exists(cli))
            return Fail(log, $"CLI not found beside Explorer helper: {cli}", args[0]);

        var start = new ProcessStartInfo
        {
            FileName = cli,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
        {
            "convert", args[0], "--collision", "counter", "--json"
        })
            start.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return Fail(log, "CLI process did not start.", args[0]);
            var stdout = DrainBoundedAsync(process.StandardOutput);
            var stderr = DrainBoundedAsync(process.StandardError);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Already exited.
                }
                return Fail(log, "CLI timed out after three minutes.", args[0]);
            }

            var output = await stdout.ConfigureAwait(false);
            var error = await stderr.ConfigureAwait(false);
            if (process.ExitCode == 0) return 0;
            return Fail(
                log,
                $"CLI exited with code {process.ExitCode}.\nstdout:\n{output}\nstderr:\n{error}",
                args[0]);
        }
        catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or System.ComponentModel.Win32Exception)
        {
            return Fail(log, exception.Message, args[0]);
        }
    }

    private static int Fail(string log, string diagnostic, string sourcePath)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            Rotate(log);
            var entry = new StringBuilder()
                .AppendLine($"[{DateTimeOffset.UtcNow:O}] MD2PDF Explorer failure")
                .AppendLine($"Source: {sourcePath}")
                .AppendLine(diagnostic)
                .AppendLine()
                .ToString();
            File.AppendAllText(log, entry, new UTF8Encoding(false));
        }
        catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException)
        {
            // The concise dialog remains useful even when logging fails.
        }

        if (!string.Equals(
                Environment.GetEnvironmentVariable("MD2PDF_EXPLORER_NO_DIALOG"),
                "1",
                StringComparison.Ordinal))
        {
            _ = MessageBox(
                0,
                $"MD2PDF could not create the PDF.\nSee log: {log}",
                "MD2PDF",
                MbIconError);
        }
        return 5;
    }

    private static async Task<string> DrainBoundedAsync(StreamReader reader)
    {
        var retained = new StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer, CancellationToken.None)
                .ConfigureAwait(false);
            if (count == 0) break;
            retained.Append(buffer, 0, count);
            if (retained.Length > CapturedCharacters)
                retained.Remove(0, retained.Length - CapturedCharacters);
        }
        return retained.ToString();
    }

    private static string GetLogPath()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(local, "md2pdf", "logs", "explorer.log");
    }

    private static void Rotate(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < MaxLogBytes) return;
        for (var index = LogBackups; index >= 1; index--)
        {
            var current = index == 1 ? path : $"{path}.{index - 1}";
            var next = $"{path}.{index}";
            if (File.Exists(current)) File.Move(current, next, overwrite: true);
        }
    }

#pragma warning disable SYSLIB1054
    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(
        nint window,
        string text,
        string caption,
        uint type);
#pragma warning restore SYSLIB1054
}
