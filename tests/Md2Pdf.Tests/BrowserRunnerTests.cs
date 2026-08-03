using Md2Pdf.Core;

namespace Md2Pdf.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class BrowserRunnerTests
{
    [Fact]
    public async Task HandlesSeparatedArgumentsAndLargeOutputWithoutDeadlock()
    {
        using var temp = new TemporaryDirectory("path with spaces");
        var html = temp.File("source file.html");
        var pdf = temp.File("output file.pdf");
        var arguments = temp.File("arguments.txt");
        await File.WriteAllTextAsync(html, "<!doctype html><h1>test</h1>");
        Environment.SetEnvironmentVariable("MD2PDF_FAKE_MODE", "large-output");
        Environment.SetEnvironmentVariable("MD2PDF_FAKE_ARGS", arguments);
        try
        {
            var result = await BrowserRunner.PrintAsync(
                FakeBrowser.Find(), html, pdf, TimeSpan.FromSeconds(10));

            Assert.True(result.Success, result.Error);
            Assert.InRange(result.StandardOutput.Length, 1, 64 * 1024);
            Assert.InRange(result.StandardError.Length, 1, 64 * 1024);
            var captured = await File.ReadAllLinesAsync(arguments);
            Assert.Contains($"--print-to-pdf={pdf}", captured);
            Assert.Contains(new Uri(Path.GetFullPath(html)).AbsoluteUri, captured);
            Assert.True(PdfValidation.IsUsable(pdf, out var diagnostic), diagnostic);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MD2PDF_FAKE_MODE", null);
            Environment.SetEnvironmentVariable("MD2PDF_FAKE_ARGS", null);
        }
    }

    [Fact]
    public async Task StopsTimedOutBrowser()
    {
        using var temp = new TemporaryDirectory();
        var html = temp.File("source.html");
        var pdf = temp.File("output.pdf");
        await File.WriteAllTextAsync(html, "<h1>test</h1>");
        Environment.SetEnvironmentVariable("MD2PDF_FAKE_MODE", "hang");
        try
        {
            var started = DateTime.UtcNow;
            var result = await BrowserRunner.PrintAsync(
                FakeBrowser.Find(), html, pdf, TimeSpan.FromMilliseconds(300));

            Assert.False(result.Success);
            Assert.Contains("0 seconds", result.Error, StringComparison.Ordinal);
            Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10));
        }
        finally
        {
            Environment.SetEnvironmentVariable("MD2PDF_FAKE_MODE", null);
        }
    }

    [Fact]
    public async Task TreatsNonzeroExitAsFailure()
    {
        using var temp = new TemporaryDirectory();
        var html = temp.File("source.html");
        var pdf = temp.File("output.pdf");
        await File.WriteAllTextAsync(html, "<h1>test</h1>");
        Environment.SetEnvironmentVariable("MD2PDF_FAKE_MODE", "fail");
        try
        {
            var result = await BrowserRunner.PrintAsync(
                FakeBrowser.Find(), html, pdf, TimeSpan.FromSeconds(5));

            Assert.False(result.Success);
            Assert.Equal(17, result.ExitCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MD2PDF_FAKE_MODE", null);
        }
    }
}
