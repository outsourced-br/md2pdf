using System.Text.Json;
using Md2Pdf.Cli;
using Md2Pdf.Core;

namespace Md2Pdf.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class CliContractTests
{
    [Fact]
    public async Task VersionDoesNotNeedBrowserDiscovery()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["--version"], output, error, CancellationToken.None);

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal("0.1.0", output.ToString().Trim());
        Assert.Equal("", error.ToString());
    }

    [Fact]
    public async Task JsonUsageFailureIsOneStructuredDocument()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            ["convert", "report.md", "--force", "--collision", "fail", "--json"],
            output,
            error,
            CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Equal("", error.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains(
            "mutually exclusive",
            json.RootElement.GetProperty("errors")[0].GetString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--paper", "Tabloid", null, null)]
    [InlineData("--json=true", null, null, null)]
    [InlineData("--output", "one.pdf", "-o", "two.pdf")]
    public async Task InvalidGrammarReturnsUsage(
        string option,
        string? value,
        string? secondOption = null,
        string? secondValue = null)
    {
        var arguments = new List<string> { "convert", "report.md", option };
        if (value is not null) arguments.Add(value);
        if (secondOption is not null) arguments.Add(secondOption);
        if (secondValue is not null) arguments.Add(secondValue);
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            [.. arguments], output, error, CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Contains("error:", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("doctor", "--unknown", "--json", null)]
    [InlineData("browser", "--json", null, null)]
    [InlineData("browser", "unknown", "--json", null)]
    [InlineData("explorer", "status", "--bad", "--json")]
    public async Task JsonManagementUsageFailureIsStructured(
        string command,
        string argument1,
        string? argument2,
        string? argument3 = null)
    {
        var arguments = new[] { command, argument1, argument2, argument3 }
            .Where(argument => argument is not null)
            .Cast<string>()
            .ToArray();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CliApplication.RunAsync(
            arguments, output, error, CancellationToken.None);

        Assert.Equal(ExitCodes.Usage, exitCode);
        Assert.Equal("", error.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.False(json.RootElement.GetProperty("success").GetBoolean());
        Assert.NotEmpty(json.RootElement.GetProperty("errors").EnumerateArray());
    }
}
