using Md2Pdf.Core;

namespace Md2Pdf.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class ConversionSafetyTests
{
    [Fact]
    public async Task RejectsNonMarkdownInput()
    {
        using var temp = new TemporaryDirectory();
        var text = temp.File("report.txt");
        await File.WriteAllTextAsync(text, "# Report");

        var result = await PdfConverter.ConvertAsync(new ConvertOptions { Input = text });

        Assert.False(result.Success);
        Assert.Equal(ExitCodes.Usage, result.ExitCode);
        Assert.Contains(".md", Assert.Single(result.Errors), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CounterUsesMatchingPdfAndHtmlSuffix()
    {
        using var temp = new TemporaryDirectory();
        var markdown = temp.File("report.md");
        await File.WriteAllTextAsync(markdown, "# Report\n\nHello");
        var browser = FakeBrowser.Find();

        var first = await PdfConverter.ConvertAsync(new ConvertOptions
        {
            Input = markdown,
            BrowserPath = browser,
            KeepHtml = true
        });
        var second = await PdfConverter.ConvertAsync(new ConvertOptions
        {
            Input = markdown,
            BrowserPath = browser,
            KeepHtml = true,
            Collision = CollisionPolicy.Counter
        });

        Assert.True(first.Success, string.Join("; ", first.Errors));
        Assert.True(second.Success, string.Join("; ", second.Errors));
        Assert.Equal(temp.File("report.pdf"), first.Output);
        Assert.Equal(temp.File("report.html"), first.Html);
        Assert.Equal(temp.File("report_0001.pdf"), second.Output);
        Assert.Equal(temp.File("report_0001.html"), second.Html);
        Assert.True(File.Exists(second.Output));
        Assert.True(File.Exists(second.Html));
    }

    [Fact]
    public async Task FailedForceConversionPreservesExistingPdf()
    {
        using var temp = new TemporaryDirectory();
        var markdown = temp.File("report.md");
        var pdf = temp.File("report.pdf");
        await File.WriteAllTextAsync(markdown, "# Report");
        var original = Enumerable.Range(0, 1024)
            .Select(index => (byte)(index % 251))
            .ToArray();
        await File.WriteAllBytesAsync(pdf, original);
        Environment.SetEnvironmentVariable("MD2PDF_FAKE_MODE", "invalid");
        try
        {
            var result = await PdfConverter.ConvertAsync(new ConvertOptions
            {
                Input = markdown,
                BrowserPath = FakeBrowser.Find(),
                Force = true
            });

            Assert.False(result.Success);
            Assert.Equal(ExitCodes.RenderFailure, result.ExitCode);
            Assert.Equal(original, await File.ReadAllBytesAsync(pdf));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MD2PDF_FAKE_MODE", null);
        }
    }

    [Fact]
    public void ReservationIsAtomicAcrossConcurrentCounterCalls()
    {
        using var temp = new TemporaryDirectory();
        var output = temp.File("report.pdf");

        using var first = OutputReservation.Reserve(
            output, keepHtml: false, force: false, CollisionPolicy.Counter);
        using var second = OutputReservation.Reserve(
            output, keepHtml: false, force: false, CollisionPolicy.Counter);

        Assert.Equal(output, first.PdfPath);
        Assert.Equal(temp.File("report_0001.pdf"), second.PdfPath);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("truncated")]
    [InlineData("no-output")]
    public async Task RejectsInvalidOrMissingBrowserOutput(string mode)
    {
        using var temp = new TemporaryDirectory();
        var html = temp.File("source.html");
        var pdf = temp.File("output.pdf");
        await File.WriteAllTextAsync(html, "<h1>test</h1>");
        Environment.SetEnvironmentVariable("MD2PDF_FAKE_MODE", mode);
        try
        {
            var print = await BrowserRunner.PrintAsync(
                FakeBrowser.Find(), html, pdf, TimeSpan.FromSeconds(5));

            Assert.False(PdfValidation.IsUsable(pdf, out _));
            if (mode == "no-output") Assert.True(print.Success);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MD2PDF_FAKE_MODE", null);
        }
    }
}
