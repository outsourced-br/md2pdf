using System.Runtime.InteropServices;
using System.Text;

namespace Md2Pdf.Core;

public static class PdfConverter
{
    public static async Task<ConversionResult> ConvertAsync(
        ConvertOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = new ConversionResult { ExitCode = ExitCodes.RenderFailure };
        string input;
        try
        {
            input = LongPath(Path.GetFullPath(options.Input));
        }
        catch (Exception exception) when (exception is ArgumentException
                                               or NotSupportedException
                                               or PathTooLongException)
        {
            result.Input = options.Input;
            result.Errors.Add(exception.Message);
            result.ExitCode = ExitCodes.Usage;
            return result;
        }

        result.Input = input;
        if (!string.Equals(
                Path.GetExtension(input),
                ".md",
                StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add("The input path must end in .md.");
            result.ExitCode = ExitCodes.Usage;
            return result;
        }
        if (!File.Exists(input))
        {
            result.Errors.Add($"Markdown not found: {input}");
            result.ExitCode = ExitCodes.Usage;
            return result;
        }
        if (options.Force && options.Collision != CollisionPolicy.Fail)
        {
            result.Errors.Add("--force and --collision counter are mutually exclusive.");
            result.ExitCode = ExitCodes.Usage;
            return result;
        }
        if (options.ManagedBrowserOnly && !string.IsNullOrWhiteSpace(options.BrowserPath))
        {
            result.Errors.Add("--managed-browser and --browser are mutually exclusive.");
            result.ExitCode = ExitCodes.Usage;
            return result;
        }

        string requestedOutput;
        try
        {
            requestedOutput = options.Output is null
                ? Path.ChangeExtension(input, ".pdf")
                : Path.GetFullPath(options.Output);
        }
        catch (Exception exception) when (exception is ArgumentException
                                               or NotSupportedException
                                               or PathTooLongException)
        {
            result.Errors.Add(exception.Message);
            result.ExitCode = ExitCodes.Usage;
            return result;
        }
        if (!string.Equals(Path.GetExtension(requestedOutput), ".pdf",
                StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add("The output path must end in .pdf.");
            result.ExitCode = ExitCodes.Usage;
            return result;
        }

        var outputDirectory = Path.GetDirectoryName(requestedOutput) ?? ".";
        try
        {
            Directory.CreateDirectory(outputDirectory);
        }
        catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException)
        {
            result.Errors.Add($"Output directory cannot be created: {exception.Message}");
            return result;
        }

        OutputReservation reservation;
        try
        {
            reservation = OutputReservation.Reserve(
                requestedOutput, options.KeepHtml, options.Force, options.Collision);
        }
        catch (IOException exception)
        {
            result.Errors.Add(exception.Message);
            result.ExitCode = ExitCodes.Usage;
            return result;
        }
        catch (UnauthorizedAccessException exception)
        {
            result.Errors.Add(exception.Message);
            return result;
        }

        using (reservation)
        {
            result.Output = reservation.PdfPath;
            result.Html = reservation.HtmlPath;
            var token = Guid.NewGuid().ToString("N");
            var tempHtml = Path.Combine(outputDirectory, $".md2pdf-{token}.html");
            var tempPdf = Path.Combine(outputDirectory, $".md2pdf-{token}.pdf");
            try
            {
                var markdown = await File.ReadAllTextAsync(input, cancellationToken)
                    .ConfigureAwait(false);
                var rendered = MarkdownDocumentRenderer.Render(
                    markdown, input, options.Paper, options.Landscape);
                result.Warnings.AddRange(rendered.Warnings);
                await File.WriteAllTextAsync(
                    tempHtml, rendered.Html, new UTF8Encoding(false), cancellationToken)
                    .ConfigureAwait(false);

                var candidates = BrowserLocator.FindCandidates(
                    options.BrowserPath, options.ManagedBrowserOnly);
                if (candidates.Count == 0)
                {
                    result.Errors.Add(
                        "No usable Chromium-family browser was found. " +
                        "Run `md2pdf browser install` or pass --browser <path>.");
                    result.ExitCode = ExitCodes.BrowserUnavailable;
                    return result;
                }

                var printAttempted = false;
                foreach (var candidate in candidates)
                {
                    if (!File.Exists(candidate.Path))
                    {
                        result.Warnings.Add($"Browser not found: {candidate.Path}");
                        continue;
                    }

                    candidate.Version = await BrowserRunner.ProbeVersionAsync(
                        candidate.Path, cancellationToken).ConfigureAwait(false);
                    if (candidate.Version is null)
                    {
                        result.Warnings.Add($"Browser could not be started: {candidate.Path}");
                        continue;
                    }

                    TryDelete(tempPdf);
                    printAttempted = true;
                    var print = await BrowserRunner.PrintAsync(
                        candidate.Path, tempHtml, tempPdf, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    if (!print.Success)
                    {
                        result.Warnings.Add(
                            $"{candidate.Name} failed: {print.Error ?? Tail(print.StandardError)}");
                        continue;
                    }
                    if (!PdfValidation.IsUsable(tempPdf, out var diagnostic))
                    {
                        result.Warnings.Add($"{candidate.Name} produced no usable PDF: {diagnostic}");
                        continue;
                    }

                    result.Browser = new BrowserInfo
                    {
                        Source = candidate.Source,
                        Name = candidate.Name,
                        Path = candidate.Path,
                        Version = candidate.Version
                    };
                    CommitOutputs(
                        tempPdf,
                        options.KeepHtml ? tempHtml : null,
                        reservation.PdfPath,
                        reservation.HtmlPath,
                        options.Force);
                    result.Bytes = new FileInfo(reservation.PdfPath).Length;
                    result.Success = true;
                    result.ExitCode = ExitCodes.Success;
                    return result;
                }

                result.Errors.Add(
                    "PDF generation failed with every discovered browser. Run `md2pdf doctor`.");
                result.ExitCode = printAttempted
                    ? ExitCodes.RenderFailure
                    : ExitCodes.BrowserUnavailable;
                return result;
            }
            catch (Exception exception) when (exception is IOException
                                                   or UnauthorizedAccessException
                                                   or ArgumentException)
            {
                result.Errors.Add(exception.Message);
                result.ExitCode = ExitCodes.RenderFailure;
                return result;
            }
            finally
            {
                TryDelete(tempHtml);
                TryDelete(tempPdf);
            }
        }
    }

    private static void CommitOutputs(
        string tempPdf,
        string? tempHtml,
        string pdf,
        string? html,
        bool replace)
    {
        var backupPdf = pdf + ".md2pdf-backup-" + Guid.NewGuid().ToString("N");
        var backupHtml = html is null
            ? null
            : html + ".md2pdf-backup-" + Guid.NewGuid().ToString("N");
        var movedPdf = false;
        var movedHtml = false;
        try
        {
            if (replace && File.Exists(pdf)) File.Move(pdf, backupPdf);
            if (replace && html is not null && File.Exists(html))
                File.Move(html, backupHtml!);

            if (tempHtml is not null && html is not null)
            {
                File.Move(tempHtml, html, overwrite: false);
                movedHtml = true;
            }
            File.Move(tempPdf, pdf, overwrite: false);
            movedPdf = true;
            TryDelete(backupPdf);
            if (backupHtml is not null) TryDelete(backupHtml);
        }
        catch
        {
            if (movedPdf) TryDelete(pdf);
            if (movedHtml && html is not null) TryDelete(html);
            if (File.Exists(backupPdf)) File.Move(backupPdf, pdf, overwrite: true);
            if (backupHtml is not null && html is not null && File.Exists(backupHtml))
                File.Move(backupHtml, html, overwrite: true);
            throw;
        }
    }

    private static string Tail(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return "no diagnostic was returned";
        return trimmed.Length <= 500 ? trimmed : trimmed[^500..];
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException)
        {
            // Temporary cleanup is best effort.
        }
    }

    private static string LongPath(string path)
    {
        if (!OperatingSystem.IsWindows() || !path.Contains('~')) return path;
        var buffer = new StringBuilder(32768);
        var length = GetLongPathName(path, buffer, buffer.Capacity);
        return length > 0 && length < buffer.Capacity ? buffer.ToString() : path;
    }

#pragma warning disable SYSLIB1054
    [DllImport("kernel32.dll", EntryPoint = "GetLongPathNameW", CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern int GetLongPathName(
        string shortPath,
        StringBuilder longPath,
        int capacity);
#pragma warning restore SYSLIB1054
}
