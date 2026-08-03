using System.Text;

if (args.Contains("--version", StringComparer.Ordinal))
{
    Console.WriteLine("Chrome Headless Shell 151.0.7922.71");
    return 0;
}

var argumentLog = Environment.GetEnvironmentVariable("MD2PDF_FAKE_ARGS");
if (!string.IsNullOrWhiteSpace(argumentLog))
    await File.WriteAllLinesAsync(argumentLog, args);

var mode = Environment.GetEnvironmentVariable("MD2PDF_FAKE_MODE") ?? "success";
if (mode.Equals("hang", StringComparison.OrdinalIgnoreCase))
{
    await Task.Delay(TimeSpan.FromMinutes(10));
    return 0;
}

if (mode.Equals("large-output", StringComparison.OrdinalIgnoreCase))
{
    Console.Out.Write(new string('o', 160_000));
    Console.Error.Write(new string('e', 160_000));
}

if (mode.Equals("fail", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("intentional fake-browser failure");
    return 17;
}

var printArgument = args.FirstOrDefault(argument =>
    argument.StartsWith("--print-to-pdf=", StringComparison.Ordinal));
if (printArgument is null)
{
    Console.Error.WriteLine("missing --print-to-pdf");
    return 18;
}

var output = printArgument["--print-to-pdf=".Length..];
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);

switch (mode.ToLowerInvariant())
{
    case "no-output":
        return 0;
    case "invalid":
        await File.WriteAllTextAsync(output, new string('x', 2048));
        return 0;
    case "truncated":
        await File.WriteAllTextAsync(
            output,
            "%PDF-1.7\n1 0 obj\n<< /Type /Page >>\nendobj\n" + new string('x', 2048));
        return 0;
    default:
        var pdf = new StringBuilder()
            .AppendLine("%PDF-1.7")
            .AppendLine("1 0 obj")
            .AppendLine("<< /Type /Catalog /Pages 2 0 R >>")
            .AppendLine("endobj")
            .AppendLine("2 0 obj")
            .AppendLine("<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .AppendLine("endobj")
            .AppendLine("3 0 obj")
            .AppendLine("<< /Type /Page /Parent 2 0 R >>")
            .AppendLine("endobj")
            .Append('%')
            .AppendLine(new string('x', 2048))
            .AppendLine("startxref")
            .AppendLine("0")
            .AppendLine("%%EOF")
            .ToString();
        await File.WriteAllTextAsync(output, pdf, Encoding.ASCII);
        return 0;
}
