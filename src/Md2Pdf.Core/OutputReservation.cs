namespace Md2Pdf.Core;

internal sealed class OutputReservation : IDisposable
{
    private readonly FileStream _lock;
    private readonly string _lockPath;

    private OutputReservation(string pdfPath, string? htmlPath, FileStream fileLock, string lockPath)
    {
        PdfPath = pdfPath;
        HtmlPath = htmlPath;
        _lock = fileLock;
        _lockPath = lockPath;
    }

    public string PdfPath { get; }
    public string? HtmlPath { get; }

    public static OutputReservation Reserve(
        string requestedPdf,
        bool keepHtml,
        bool force,
        CollisionPolicy collision)
    {
        var directory = Path.GetDirectoryName(requestedPdf) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(requestedPdf);
        var extension = Path.GetExtension(requestedPdf);
        for (var counter = 0; counter <= 9999; counter++)
        {
            var suffix = counter == 0 ? "" : $"_{counter:0000}";
            var pdf = Path.Combine(directory, stem + suffix + extension);
            var html = keepHtml ? Path.Combine(directory, stem + suffix + ".html") : null;
            if (!force && collision == CollisionPolicy.Fail &&
                (File.Exists(pdf) || (html is not null && File.Exists(html))))
                throw new IOException(
                    $"{Path.GetFileName(pdf)} already exists. Pass --force or --collision counter.");

            if (!force && collision == CollisionPolicy.Counter &&
                (File.Exists(pdf) || (html is not null && File.Exists(html))))
                continue;

            var lockPath = pdf + ".md2pdf.lock";
            TryRemoveStaleLock(lockPath);
            try
            {
                var fileLock = new FileStream(
                    lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                if (!force &&
                    (File.Exists(pdf) || (html is not null && File.Exists(html))))
                {
                    fileLock.Dispose();
                    File.Delete(lockPath);
                    if (collision == CollisionPolicy.Counter) continue;
                    throw new IOException($"{Path.GetFileName(pdf)} was created concurrently.");
                }
                return new OutputReservation(pdf, html, fileLock, lockPath);
            }
            catch (IOException) when (collision == CollisionPolicy.Counter && !force)
            {
                continue;
            }
        }
        throw new IOException("No free output name remained between _0001 and _9999.");
    }

    public void Dispose()
    {
        _lock.Dispose();
        try
        {
            File.Delete(_lockPath);
        }
        catch (IOException)
        {
            // A stale lock is ignored after one day by the next run.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup only.
        }
    }

    private static void TryRemoveStaleLock(string path)
    {
        try
        {
            if (File.Exists(path) &&
                DateTime.UtcNow - File.GetLastWriteTimeUtc(path) > TimeSpan.FromDays(1))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException)
        {
            // The subsequent CreateNew call remains the authority.
        }
    }
}
