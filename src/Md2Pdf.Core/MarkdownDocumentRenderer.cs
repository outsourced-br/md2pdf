using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Markdig;

namespace Md2Pdf.Core;

public sealed record HtmlRenderResult(string Html, IReadOnlyList<string> Warnings);

public static partial class MarkdownDocumentRenderer
{
    private const long MaxImageBytes = 25L * 1024 * 1024;
    private const long MaxTotalImageBytes = 100L * 1024 * 1024;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseTaskLists()
        .UseAutoLinks()
        .UseEmphasisExtras()
        .DisableHtml()
        .Build();

    public static HtmlRenderResult Render(
        string markdown,
        string sourcePath,
        PaperSize paper,
        bool landscape)
    {
        var warnings = new List<string>();
        var frontmatter = ReadFrontmatter(markdown, warnings);
        var body = Markdown.ToHtml(frontmatter.Body, Pipeline);
        body = InlineImages(body, Path.GetDirectoryName(sourcePath) ?? ".", warnings);
        var title = frontmatter.Title ?? Path.GetFileNameWithoutExtension(sourcePath);
        var metadata = RenderMetadata(frontmatter.Items);
        var page = paper switch
        {
            PaperSize.A4 => "A4",
            PaperSize.Letter => "Letter",
            PaperSize.Legal => "Legal",
            _ => throw new ArgumentOutOfRangeException(nameof(paper))
        };
        if (landscape) page += " landscape";

        var html = Shell
            .Replace("@PAGE@", page, StringComparison.Ordinal)
            .Replace("@TITLE@", WebUtility.HtmlEncode(title), StringComparison.Ordinal)
            .Replace("@METADATA@", metadata, StringComparison.Ordinal)
            .Replace("@BODY@", body, StringComparison.Ordinal);
        return new HtmlRenderResult(html, warnings);
    }

    private sealed record FrontmatterResult(
        string Body,
        string? Title,
        List<KeyValuePair<string, string>> Items);

    private static FrontmatterResult ReadFrontmatter(string markdown, List<string> warnings)
    {
        var normalized = markdown.ReplaceLineEndings("\n");
        var lines = normalized.Split('\n');
        if (lines.Length == 0 || !string.Equals(lines[0].Trim(), "---", StringComparison.Ordinal))
            return new FrontmatterResult(normalized, null, []);

        var end = -1;
        for (var index = 1; index < lines.Length; index++)
        {
            var marker = lines[index].Trim();
            if (marker is "---" or "...")
            {
                end = index;
                break;
            }
        }
        if (end < 0)
        {
            warnings.Add("YAML frontmatter starts but has no closing marker; rendered as Markdown.");
            return new FrontmatterResult(normalized, null, []);
        }

        var items = new List<KeyValuePair<string, string>>();
        string? currentKey = null;
        var currentValues = new List<string>();

        void Flush()
        {
            if (currentKey is null) return;
            items.Add(new KeyValuePair<string, string>(
                currentKey, string.Join("; ", currentValues)));
            currentKey = null;
            currentValues.Clear();
        }

        for (var index = 1; index < end; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line)) continue;
            var pair = FrontmatterPairRegex().Match(line);
            if (pair.Success)
            {
                Flush();
                currentKey = pair.Groups["key"].Value;
                var value = pair.Groups["value"].Value.Trim();
                if (value.Length > 0) currentValues.Add(Unquote(value));
                continue;
            }

            var listItem = FrontmatterListRegex().Match(line);
            if (listItem.Success && currentKey is not null)
            {
                currentValues.Add(Unquote(listItem.Groups["value"].Value.Trim()));
                continue;
            }

            warnings.Add($"Unsupported YAML frontmatter line {index + 1} was escaped as metadata.");
            currentKey ??= "unparsed";
            currentValues.Add(line.Trim());
        }
        Flush();

        var title = items.FirstOrDefault(item =>
            string.Equals(item.Key, "title", StringComparison.OrdinalIgnoreCase)).Value;
        var body = string.Join('\n', lines.Skip(end + 1));
        return new FrontmatterResult(body, string.IsNullOrWhiteSpace(title) ? null : title, items);
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') ||
             (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1];
        return value;
    }

    private static string RenderMetadata(List<KeyValuePair<string, string>> items)
    {
        if (items.Count == 0) return "";
        var builder = new StringBuilder();
        builder.Append("<section class=\"meta\"><div class=\"meta-h\">Document metadata</div>")
            .Append("<table class=\"metatbl\"><tbody>");
        foreach (var item in items)
        {
            builder.Append("<tr><th>")
                .Append(WebUtility.HtmlEncode(item.Key.Replace('_', ' ')))
                .Append("</th><td>")
                .Append(WebUtility.HtmlEncode(item.Value))
                .Append("</td></tr>");
        }
        return builder.Append("</tbody></table></section>").ToString();
    }

    private static string InlineImages(
        string html,
        string sourceDirectory,
        List<string> warnings)
    {
        long totalBytes = 0;
        return ImageRegex().Replace(html, match =>
        {
            var attributes = match.Groups["attributes"].Value;
            var sourceMatch = SourceAttributeRegex().Match(attributes);
            if (!sourceMatch.Success) return match.Value;

            var source = WebUtility.HtmlDecode(sourceMatch.Groups["value"].Value);
            var altMatch = AltAttributeRegex().Match(attributes);
            var alt = altMatch.Success
                ? WebUtility.HtmlDecode(altMatch.Groups["value"].Value)
                : "image";

            if (Uri.TryCreate(source, UriKind.Absolute, out var absolute) &&
                absolute.Scheme is "http" or "https")
            {
                warnings.Add($"Remote image omitted: {source}");
                return OmittedImage(alt, "remote image omitted");
            }

            if (source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                if (source.StartsWith("data:image/svg", StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add("Inline SVG data image omitted by the safe renderer.");
                    return OmittedImage(alt, "unsafe image omitted");
                }
                return match.Value;
            }

            try
            {
                string path;
                if (absolute is not null && absolute.IsFile)
                {
                    path = absolute.LocalPath;
                }
                else
                {
                    var clean = source.Split(['#', '?'], 2)[0];
                    clean = Uri.UnescapeDataString(clean).Replace(
                        '/', Path.DirectorySeparatorChar);
                    path = Path.GetFullPath(Path.Combine(sourceDirectory, clean));
                }

                if (!File.Exists(path))
                {
                    warnings.Add($"Local image not found: {source}");
                    return OmittedImage(alt, "image not found");
                }

                var info = new FileInfo(path);
                if (info.Length > MaxImageBytes || totalBytes + info.Length > MaxTotalImageBytes)
                {
                    warnings.Add($"Local image exceeds the safe size limit: {source}");
                    return OmittedImage(alt, "image too large");
                }

                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension == ".svg")
                {
                    var svg = ReadSafeSvg(path);
                    if (svg is null)
                    {
                        warnings.Add($"Unsafe or malformed SVG omitted: {source}");
                        return OmittedImage(alt, "unsafe SVG omitted");
                    }
                    totalBytes += info.Length;
                    return ReplaceSource(match.Value,
                        "data:image/svg+xml;base64," +
                        Convert.ToBase64String(Encoding.UTF8.GetBytes(svg)));
                }

                var bytes = File.ReadAllBytes(path);
                var mime = DetectRasterMime(bytes);
                if (mime is null)
                {
                    warnings.Add($"Unsupported or invalid local image omitted: {source}");
                    return OmittedImage(alt, "unsupported image omitted");
                }

                totalBytes += bytes.Length;
                return ReplaceSource(match.Value,
                    $"data:{mime};base64,{Convert.ToBase64String(bytes)}");
            }
            catch (Exception exception) when (exception is IOException
                                                   or UnauthorizedAccessException
                                                   or ArgumentException
                                                   or UriFormatException
                                                   or XmlException)
            {
                warnings.Add($"Local image could not be read ({source}): {exception.Message}");
                return OmittedImage(alt, "image unavailable");
            }
        });
    }

    private static string? ReadSafeSvg(string path)
    {
        var text = File.ReadAllText(path);
        XDocument document;
        try
        {
            using var stringReader = new StringReader(text);
            using var xmlReader = XmlReader.Create(
                stringReader,
                new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                });
            document = XDocument.Load(xmlReader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return null;
        }
        if (!string.Equals(
                document.Root?.Name.LocalName,
                "svg",
                StringComparison.OrdinalIgnoreCase))
            return null;

        foreach (var element in document.Descendants())
        {
            var name = element.Name.LocalName;
            if (name.Equals("script", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("foreignObject", StringComparison.OrdinalIgnoreCase))
                return null;

            if (name.Equals("style", StringComparison.OrdinalIgnoreCase) &&
                (element.Value.Contains("@import", StringComparison.OrdinalIgnoreCase) ||
                 element.Value.Contains("@font-face", StringComparison.OrdinalIgnoreCase) ||
                 ContainsExternalCssReference(element.Value)))
                return null;

            foreach (var attribute in element.Attributes())
            {
                var attributeName = attribute.Name.LocalName;
                if (attributeName.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                    return null;
                if ((attributeName.Equals("href", StringComparison.OrdinalIgnoreCase) ||
                     attributeName.Equals("src", StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrWhiteSpace(attribute.Value) &&
                    !attribute.Value.StartsWith('#') &&
                    !attribute.Value.StartsWith(
                        "data:image/",
                        StringComparison.OrdinalIgnoreCase))
                    return null;
                if (attributeName.Equals("style", StringComparison.OrdinalIgnoreCase) &&
                    (attribute.Value.Contains("@import", StringComparison.OrdinalIgnoreCase) ||
                     attribute.Value.Contains("@font-face", StringComparison.OrdinalIgnoreCase) ||
                     ContainsExternalCssReference(attribute.Value)))
                    return null;
            }
        }
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static bool ContainsExternalCssReference(string css)
    {
        var offset = 0;
        while ((offset = css.IndexOf("url(", offset, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var start = offset + 4;
            var end = css.IndexOf(')', start);
            if (end < 0) return true;
            var target = css[start..end].Trim().Trim('\'', '"');
            if (!target.StartsWith('#')) return true;
            offset = end + 1;
        }
        return false;
    }

    private static string? DetectRasterMime(byte[] bytes)
    {
        if (bytes.Length >= 8 &&
            bytes.AsSpan(0, 8).SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
            return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff)
            return "image/jpeg";
        if (bytes.Length >= 6 &&
            (Encoding.ASCII.GetString(bytes, 0, 6) is "GIF87a" or "GIF89a"))
            return "image/gif";
        if (bytes.Length >= 12 &&
            Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" &&
            Encoding.ASCII.GetString(bytes, 8, 4) == "WEBP")
            return "image/webp";
        if (bytes.Length >= 2 && bytes[0] == 'B' && bytes[1] == 'M')
            return "image/bmp";
        return null;
    }

    private static string ReplaceSource(string imageTag, string replacement) =>
        SourceAttributeRegex().Replace(
            imageTag,
            match => $"src=\"{WebUtility.HtmlEncode(replacement)}\"",
            1);

    private static string OmittedImage(string alt, string reason) =>
        $"<span class=\"image-omitted\">[{WebUtility.HtmlEncode(reason)}: " +
        $"{WebUtility.HtmlEncode(alt)}]</span>";

    private const string Shell = """
        <!doctype html>
        <html lang="en"><head><meta charset="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <title>@TITLE@</title>
        <style>
          @page { size: @PAGE@; margin: 17mm 15mm 17mm 15mm; }
          * { box-sizing: border-box; }
          html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
          body { font-family: "Segoe UI", Calibri, Arial, sans-serif; font-size: 9.6pt;
                 line-height: 1.45; color: #1a1a1a; margin: 0; }
          h1 { font-size: 18pt; margin: 0 0 4mm; padding-bottom: 2mm; border-bottom: 2px solid #333;
               line-height: 1.2; }
          h2 { font-size: 12.5pt; margin: 7mm 0 2.5mm; padding-bottom: 1mm;
               border-bottom: 1px solid #ccc; break-after: avoid; page-break-after: avoid; }
          h3 { font-size: 10.6pt; margin: 5mm 0 2mm; break-after: avoid; page-break-after: avoid; }
          h4 { font-size: 9.8pt; margin: 4mm 0 1.5mm; }
          p { margin: 0 0 2.2mm; }
          ul, ol { margin: 0 0 2.5mm; padding-left: 6mm; }
          li { margin-bottom: 1.1mm; }
          strong { font-weight: 600; }
          code { font-family: Consolas, "Courier New", monospace; font-size: 8.6pt;
                 background: #f2f3f5; padding: 0.3mm 1mm; border-radius: 2px; overflow-wrap: anywhere; }
          pre { font-family: Consolas, "Courier New", monospace; font-size: 8.4pt; background: #f6f7f9;
                border: 1px solid #e0e2e6; border-radius: 3px; padding: 2.5mm; overflow-wrap: break-word;
                white-space: pre-wrap; margin: 0 0 3mm; }
          pre code { padding: 0; background: transparent; }
          a { color: #14456b; text-decoration: none; overflow-wrap: anywhere; }
          img { max-width: 100%; height: auto; break-inside: avoid; }
          .image-omitted { color: #68707d; font-style: italic; }
          blockquote { margin: 0 0 3.5mm; padding: 2.5mm 3.5mm; background: #f7f8fa;
                       border-left: 3px solid #8a94a6; }
          blockquote p:last-child { margin-bottom: 0; }
          hr { border: 0; border-top: 1px solid #d5d8dd; margin: 5mm 0; }
          table { width: 100%; border-collapse: collapse; margin: 0 0 4mm; font-size: 8.5pt;
                  table-layout: auto; }
          thead { display: table-header-group; }
          tr { break-inside: avoid; page-break-inside: avoid; }
          th, td { border: 1px solid #ccd0d6; padding: 1.3mm 1.8mm; vertical-align: top;
                   overflow-wrap: break-word; word-break: normal; }
          th { background: #eceef1; font-weight: 600; text-align: left; }
          tbody tr:nth-child(even) { background: #fafbfc; }
          td code { white-space: normal; }
          input[type="checkbox"] { margin-right: 1.5mm; }
          .meta { margin: 0 0 6mm; padding: 3mm; background: #f7f8fa; border: 1px solid #e2e5ea;
                  border-radius: 3px; break-inside: avoid; }
          .meta-h { font-size: 8pt; font-weight: 700; text-transform: uppercase;
                    letter-spacing: 0.5px; color: #5a6472; margin-bottom: 2mm; }
          table.metatbl { margin: 0; font-size: 8.2pt; }
          table.metatbl th { width: 32mm; background: transparent; border: 0;
                             border-bottom: 1px solid #e6e8ec; color: #48505c; }
          table.metatbl td { border: 0; border-bottom: 1px solid #e6e8ec; }
          table.metatbl tbody tr:nth-child(even) { background: transparent; }
        </style></head><body>
        @METADATA@
        @BODY@
        </body></html>
        """;

    [GeneratedRegex(
        @"^(?<key>[A-Za-z_][A-Za-z0-9_.-]*):\s*(?<value>.*)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex FrontmatterPairRegex();

    [GeneratedRegex(
        @"^\s*-\s+(?<value>.+)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex FrontmatterListRegex();

    [GeneratedRegex(
        @"<img\s+(?<attributes>[^>]*?)\s*/?>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ImageRegex();

    [GeneratedRegex(
        @"\bsrc\s*=\s*""(?<value>[^""]*)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SourceAttributeRegex();

    [GeneratedRegex(
        @"\balt\s*=\s*""(?<value>[^""]*)""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AltAttributeRegex();
}
