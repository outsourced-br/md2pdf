using Md2Pdf.Core;

namespace Md2Pdf.Tests;

public sealed class MarkdownRendererTests
{
    [Fact]
    public void RendersGfmFeaturesAndDisablesRawHtml()
    {
        const string markdown = """
            # Heading

            | A | B |
            |---|---|
            | 1 | 2 |

            - [x] done

            ~~gone~~

            <script>alert('no')</script>

            ```csharp
            Console.WriteLine("hello");
            ```
            """;

        var result = MarkdownDocumentRenderer.Render(
            markdown, @"C:\work\report.md", PaperSize.A4, landscape: false);

        Assert.Contains("<table>", result.Html, StringComparison.Ordinal);
        Assert.Contains("type=\"checkbox\"", result.Html, StringComparison.Ordinal);
        Assert.Contains("<del>gone</del>", result.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", result.Html, StringComparison.Ordinal);
        Assert.Contains("<pre><code class=\"language-csharp\">", result.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void RendersSimpleFrontmatterAndEscapesUnsupportedLines()
    {
        const string markdown = """
            ---
            title: "Quarterly <Review>"
            owners:
              - Alice
              - Bob
              nested: rejected
            ---
            # Body
            """;

        var result = MarkdownDocumentRenderer.Render(
            markdown, "/work/report.md", PaperSize.Letter, landscape: true);

        Assert.Contains("<title>Quarterly &lt;Review&gt;</title>", result.Html, StringComparison.Ordinal);
        Assert.Contains("Alice; Bob; nested: rejected", result.Html, StringComparison.Ordinal);
        Assert.Contains("@page { size: Letter landscape;", result.Html, StringComparison.Ordinal);
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("Unsupported YAML", StringComparison.Ordinal));
    }

    [Fact]
    public void NormalizesLineEndingsDeterministically()
    {
        var lf = MarkdownDocumentRenderer.Render(
            "# One\n\nText\n", "/tmp/a.md", PaperSize.A4, landscape: false);
        var crlf = MarkdownDocumentRenderer.Render(
            "# One\r\n\r\nText\r\n", "/tmp/a.md", PaperSize.A4, landscape: false);

        Assert.Equal(lf.Html, crlf.Html);
        Assert.Equal(lf.Warnings, crlf.Warnings);
    }

    [Fact]
    public void InlinesLocalRasterAndOmitsRemoteImage()
    {
        using var temp = new TemporaryDirectory();
        var markdownPath = temp.File("report.md");
        var imagePath = temp.File("pixel.png");
        File.WriteAllBytes(
            imagePath,
            [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00]);

        var result = MarkdownDocumentRenderer.Render(
            "![local](pixel.png)\n\n![remote](https://example.test/tracker.png)",
            markdownPath,
            PaperSize.Legal,
            landscape: false);

        Assert.Contains("data:image/png;base64,", result.Html, StringComparison.Ordinal);
        Assert.Contains("[remote image omitted: remote]", result.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("https://example.test", result.Html, StringComparison.Ordinal);
        Assert.Single(result.Warnings);
        Assert.Contains("Remote image omitted", result.Warnings[0], StringComparison.Ordinal);
        Assert.Contains("@page { size: Legal;", result.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void EmitsNoExternalStylesScriptsOrFonts()
    {
        var result = MarkdownDocumentRenderer.Render(
            "# Offline", "/tmp/a.md", PaperSize.A4, landscape: false);

        Assert.DoesNotContain("<script", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@import", result.Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("url(http", result.Html, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><image href=\"https://example.test/a.png\" /></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect style=\"fill:url( 'https://example.test/a.svg' )\" /></svg>")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><style>@font-face { src: url(data:font/woff;base64,AA==) }</style></svg>")]
    [InlineData("<!DOCTYPE svg [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><svg xmlns=\"http://www.w3.org/2000/svg\">&xxe;</svg>")]
    public void OmitsSvgWithExternalOrActiveReferences(string svg)
    {
        using var temp = new TemporaryDirectory();
        File.WriteAllText(temp.File("unsafe.svg"), svg);

        var result = MarkdownDocumentRenderer.Render(
            "![unsafe](unsafe.svg)",
            temp.File("report.md"),
            PaperSize.A4,
            landscape: false);

        Assert.Contains("[unsafe SVG omitted: unsafe]", result.Html, StringComparison.Ordinal);
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("Unsafe or malformed SVG", StringComparison.Ordinal));
    }
}
