using System.Text;

namespace Md2Pdf.Core;

public static class PdfValidation
{
    public static bool IsUsable(string path, out string diagnostic)
    {
        diagnostic = "";
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                diagnostic = "The browser produced no PDF.";
                return false;
            }
            if (info.Length < 1024)
            {
                diagnostic = "The browser produced a PDF smaller than 1 KiB.";
                return false;
            }

            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[5];
            if (stream.Read(header) != header.Length ||
                !header.SequenceEqual("%PDF-"u8))
            {
                diagnostic = "The browser output does not have a PDF header.";
                return false;
            }

            stream.Seek(0, SeekOrigin.Begin);
            var structureLength = (int)Math.Min(8 * 1024 * 1024, stream.Length);
            var structure = new byte[structureLength];
            stream.ReadExactly(structure);
            if (!Encoding.ASCII.GetString(structure)
                    .Contains("/Type /Page", StringComparison.Ordinal))
            {
                diagnostic = "The browser output contains no PDF page object.";
                return false;
            }

            var tailLength = (int)Math.Min(4096, stream.Length);
            stream.Seek(-tailLength, SeekOrigin.End);
            var tail = new byte[tailLength];
            stream.ReadExactly(tail);
            var tailText = Encoding.ASCII.GetString(tail);
            if (!tailText.Contains("startxref", StringComparison.Ordinal))
            {
                diagnostic = "The browser output has no PDF cross-reference pointer.";
                return false;
            }
            if (!tailText.Contains("%%EOF", StringComparison.Ordinal))
            {
                diagnostic = "The browser output has no PDF end marker.";
                return false;
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or ArgumentException)
        {
            diagnostic = exception.Message;
            return false;
        }
    }
}
