using System.Reflection;
using System.Text.Json;
using Md2Pdf.Core;

namespace Md2Pdf.Cli;

public static class CliApplication
{
    public static string Version =>
        typeof(CliApplication).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0] ?? "0.1.0";

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        try
        {
            if (args.Length == 0)
            {
                await stderr.WriteLineAsync("Missing command or Markdown input.").ConfigureAwait(false);
                await stderr.WriteLineAsync(Usage).ConfigureAwait(false);
                return ExitCodes.Usage;
            }
            if (args is ["--version"] or ["-v"])
            {
                await stdout.WriteLineAsync(Version).ConfigureAwait(false);
                return ExitCodes.Success;
            }
            if (args is ["--help"] or ["-h"] or ["help"])
            {
                await stdout.WriteLineAsync(Usage).ConfigureAwait(false);
                return ExitCodes.Success;
            }

            return args[0].ToLowerInvariant() switch
            {
                "doctor" => await RunDoctorAsync(args[1..], stdout, stderr, cancellationToken)
                    .ConfigureAwait(false),
                "browser" => await RunBrowserAsync(args[1..], stdout, stderr, cancellationToken)
                    .ConfigureAwait(false),
                "explorer" => await RunExplorerAsync(args[1..], stdout, stderr)
                    .ConfigureAwait(false),
                "convert" => await RunConvertAsync(args[1..], stdout, stderr, cancellationToken)
                    .ConfigureAwait(false),
                _ when !args[0].StartsWith('-') =>
                    await RunConvertAsync(args, stdout, stderr, cancellationToken)
                        .ConfigureAwait(false),
                _ when WantsJson(args) =>
                    await WriteStructuredFailureAsync(
                        args,
                        $"Unknown command or option: {args[0]}",
                        ExitCodes.Usage,
                        stdout).ConfigureAwait(false),
                _ => await UsageErrorAsync(
                        $"Unknown command or option: {args[0]}", stderr)
                    .ConfigureAwait(false)
            };
        }
        catch (OperationCanceledException)
        {
            if (WantsJson(args))
                return await WriteStructuredFailureAsync(
                    args,
                    "Operation cancelled.",
                    ExitCodes.InternalFailure,
                    stdout).ConfigureAwait(false);
            await stderr.WriteLineAsync("Operation cancelled.").ConfigureAwait(false);
            return ExitCodes.InternalFailure;
        }
        catch (Exception exception)
        {
            if (WantsJson(args))
                return await WriteStructuredFailureAsync(
                    args,
                    $"Unexpected failure: {exception.Message}",
                    ExitCodes.InternalFailure,
                    stdout).ConfigureAwait(false);
            await stderr.WriteLineAsync($"Unexpected failure: {exception.Message}")
                .ConfigureAwait(false);
            return ExitCodes.InternalFailure;
        }
    }

    private static async Task<int> RunConvertAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        var jsonRequested = args.Any(argument =>
            argument.Equals("--json", StringComparison.Ordinal));
        var parsed = ParseConvert(args);
        if (parsed.Error is not null)
        {
            if (jsonRequested)
            {
                var failure = new ConversionResult { ExitCode = ExitCodes.Usage };
                failure.Errors.Add(parsed.Error);
                await WriteJsonAsync(
                    failure,
                    Md2PdfJsonContext.Default.ConversionResult,
                    stdout).ConfigureAwait(false);
                return ExitCodes.Usage;
            }
            return await UsageErrorAsync(parsed.Error, stderr).ConfigureAwait(false);
        }
        if (parsed.Help)
        {
            await stdout.WriteLineAsync(ConvertUsage).ConfigureAwait(false);
            return ExitCodes.Success;
        }

        var result = await PdfConverter.ConvertAsync(parsed.Options!, cancellationToken)
            .ConfigureAwait(false);
        if (parsed.Json)
        {
            await WriteJsonAsync(
                result,
                Md2PdfJsonContext.Default.ConversionResult,
                stdout).ConfigureAwait(false);
        }
        else
        {
            foreach (var warning in result.Warnings)
                await stderr.WriteLineAsync($"warning: {warning}").ConfigureAwait(false);
            if (result.Success)
            {
                await stdout.WriteLineAsync(
                    $"Created: {result.Output} ({result.Bytes} bytes) using " +
                    $"{result.Browser?.Name} {result.Browser?.Version}")
                    .ConfigureAwait(false);
                if (result.Html is not null)
                    await stdout.WriteLineAsync($"HTML: {result.Html}").ConfigureAwait(false);
            }
            else
            {
                foreach (var error in result.Errors)
                    await stderr.WriteLineAsync($"error: {error}").ConfigureAwait(false);
            }
        }
        return result.ExitCode;
    }

    private static async Task<int> RunDoctorAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        var json = WantsJson(args);
        foreach (var argument in args)
        {
            if (argument == "--json") continue;
            else if (argument is "--help" or "-h")
            {
                await stdout.WriteLineAsync("Usage: md2pdf doctor [--json]").ConfigureAwait(false);
                return ExitCodes.Success;
            }
            else
            {
                if (json)
                    return await WriteStructuredFailureAsync(
                        ["doctor", .. args],
                        $"Unknown doctor option: {argument}",
                        ExitCodes.Usage,
                        stdout).ConfigureAwait(false);
                return await UsageErrorAsync($"Unknown doctor option: {argument}", stderr)
                    .ConfigureAwait(false);
            }
        }

        var result = new DoctorResult
        {
            Version = Version,
            ManagedBrowserRoot = ProductPaths.ManagedBrowserRoot,
            LogDirectory = ProductPaths.LogDirectory,
            ExitCode = ExitCodes.BrowserUnavailable
        };
        var managedState = ManagedBrowserManager.Status();
        result.ManagedBrowserInstalled = managedState.Installed;
        result.ManagedBrowserPath = managedState.Path;
        result.ManagedBrowserRootWritable = TestWritableDirectory(result.ManagedBrowserRoot);
        result.LogDirectoryWritable = TestWritableDirectory(result.LogDirectory);
        if (!result.ManagedBrowserRootWritable)
            result.Warnings.Add("The managed-browser cache is not writable.");
        if (!result.LogDirectoryWritable)
            result.Warnings.Add("The diagnostic log directory is not writable.");
        if (OperatingSystem.IsWindows())
            result.ExplorerInstalled = ExplorerIntegration.Status().Installed;

        var candidates = BrowserLocator.FindCandidates();
        foreach (var candidate in candidates)
        {
            candidate.Version = await BrowserRunner.ProbeVersionAsync(
                candidate.Path, cancellationToken).ConfigureAwait(false);
            candidate.Usable = candidate.Version is not null;
            candidate.Diagnostic = candidate.Usable
                ? null
                : "Version probe failed.";
            result.Browsers.Add(candidate);
        }

        var selected = result.Browsers.FirstOrDefault(candidate => candidate.Usable);
        if (selected is null)
        {
            result.Errors.Add(
                "No usable Chromium-family browser found. Run `md2pdf browser install`.");
        }
        else
        {
            result.SelectedBrowser = new BrowserInfo
            {
                Source = selected.Source,
                Name = selected.Name,
                Path = selected.Path,
                Version = selected.Version
            };
            result.PrintProbeSucceeded = await PrintProbeAsync(
                selected.Path, cancellationToken).ConfigureAwait(false);
            if (result.PrintProbeSucceeded)
            {
                result.Success = true;
                result.ExitCode = ExitCodes.Success;
            }
            else
            {
                result.Errors.Add("The selected browser failed a local PDF print probe.");
            }
        }

        if (json)
        {
            await WriteJsonAsync(result, Md2PdfJsonContext.Default.DoctorResult, stdout)
                .ConfigureAwait(false);
        }
        else
        {
            await stdout.WriteLineAsync($"md2pdf {result.Version}").ConfigureAwait(false);
            await stdout.WriteLineAsync(
                result.SelectedBrowser is null
                    ? "Browser: unavailable"
                    : $"Browser: {result.SelectedBrowser.Name} " +
                      $"{result.SelectedBrowser.Version} ({result.SelectedBrowser.Source})")
                .ConfigureAwait(false);
            await stdout.WriteLineAsync(
                $"Print probe: {(result.PrintProbeSucceeded ? "ok" : "failed")}")
                .ConfigureAwait(false);
            await stdout.WriteLineAsync($"Managed browser root: {result.ManagedBrowserRoot}")
                .ConfigureAwait(false);
            await stdout.WriteLineAsync(
                $"Managed browser: {(result.ManagedBrowserInstalled ? "installed" : "not installed")}")
                .ConfigureAwait(false);
            await stdout.WriteLineAsync(
                $"Writable locations: browser cache=" +
                $"{(result.ManagedBrowserRootWritable ? "ok" : "failed")}, logs=" +
                $"{(result.LogDirectoryWritable ? "ok" : "failed")}")
                .ConfigureAwait(false);
            if (result.ExplorerInstalled is not null)
                await stdout.WriteLineAsync(
                    $"Explorer integration: {(result.ExplorerInstalled.Value ? "installed" : "not installed")}")
                    .ConfigureAwait(false);
            foreach (var error in result.Errors)
                await stderr.WriteLineAsync($"error: {error}").ConfigureAwait(false);
            foreach (var warning in result.Warnings)
                await stderr.WriteLineAsync($"warning: {warning}").ConfigureAwait(false);
        }
        return result.ExitCode;
    }

    private static async Task<int> RunBrowserAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken)
    {
        var json = args.Any(argument => argument == "--json");
        if (args.Length == 0 || args[0] == "--json")
        {
            if (json)
                return await WriteStructuredFailureAsync(
                    ["browser", .. args],
                    "Missing browser action: install, remove, or status.",
                    ExitCodes.Usage,
                    stdout).ConfigureAwait(false);
            return await UsageErrorAsync(
                "Missing browser action: install, remove, or status.", stderr)
                .ConfigureAwait(false);
        }
        if (args.Skip(1).Any(argument => argument != "--json"))
        {
            if (json)
                return await WriteStructuredFailureAsync(
                    ["browser", .. args],
                    "Unknown browser option.",
                    ExitCodes.Usage,
                    stdout).ConfigureAwait(false);
            return await UsageErrorAsync("Unknown browser option.", stderr).ConfigureAwait(false);
        }

        BrowserManagementResult result;
        switch (args[0].ToLowerInvariant())
        {
            case "install":
                result = await ManagedBrowserManager.InstallAsync(cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "remove":
                result = ManagedBrowserManager.Remove();
                break;
            case "status":
                result = ManagedBrowserManager.Status();
                break;
            default:
                if (json)
                    return await WriteStructuredFailureAsync(
                        ["browser", .. args],
                        $"Unknown browser action: {args[0]}",
                        ExitCodes.Usage,
                        stdout).ConfigureAwait(false);
                return await UsageErrorAsync(
                    $"Unknown browser action: {args[0]}", stderr).ConfigureAwait(false);
        }

        if (json)
        {
            await WriteJsonAsync(
                result,
                Md2PdfJsonContext.Default.BrowserManagementResult,
                stdout).ConfigureAwait(false);
        }
        else if (result.Success)
        {
            await stdout.WriteLineAsync(
                result.Installed
                    ? $"Managed browser {result.Version}: {result.Path}"
                    : $"Managed browser {result.Version}: not installed")
                .ConfigureAwait(false);
        }
        else
        {
            foreach (var error in result.Errors)
                await stderr.WriteLineAsync($"error: {error}").ConfigureAwait(false);
        }
        return result.ExitCode;
    }

    private static async Task<int> RunExplorerAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr)
    {
        var json = args.Any(argument => argument == "--json");
        if (args.Length == 0 || args[0] == "--json")
        {
            if (json)
                return await WriteStructuredFailureAsync(
                    ["explorer", .. args],
                    "Missing explorer action: install, remove, or status.",
                    ExitCodes.Usage,
                    stdout).ConfigureAwait(false);
            return await UsageErrorAsync(
                "Missing explorer action: install, remove, or status.", stderr)
                .ConfigureAwait(false);
        }
        if (args.Skip(1).Any(argument => argument != "--json"))
        {
            if (json)
                return await WriteStructuredFailureAsync(
                    ["explorer", .. args],
                    "Unknown explorer option.",
                    ExitCodes.Usage,
                    stdout).ConfigureAwait(false);
            return await UsageErrorAsync("Unknown explorer option.", stderr).ConfigureAwait(false);
        }

        var result = args[0].ToLowerInvariant() switch
        {
            "install" => ExplorerIntegration.Install(AppContext.BaseDirectory),
            "remove" => ExplorerIntegration.Remove(),
            "status" => ExplorerIntegration.Status(),
            _ => new ExplorerManagementResult
            {
                Action = args[0],
                ExitCode = ExitCodes.Usage
            }
        };
        if (result.ExitCode == ExitCodes.Usage)
            result.Errors.Add($"Unknown explorer action: {args[0]}");

        if (json)
        {
            await WriteJsonAsync(
                result,
                Md2PdfJsonContext.Default.ExplorerManagementResult,
                stdout).ConfigureAwait(false);
        }
        else if (result.Success)
        {
            await stdout.WriteLineAsync(
                result.Installed
                    ? $"Explorer integration installed: {result.Command}"
                    : "Explorer integration is not installed.")
                .ConfigureAwait(false);
        }
        else
        {
            foreach (var error in result.Errors)
                await stderr.WriteLineAsync($"error: {error}").ConfigureAwait(false);
        }
        return result.ExitCode;
    }

    private static ParsedConvert ParseConvert(string[] args)
    {
        if (args.Length == 0) return ParsedConvert.Failure("Missing Markdown input.");
        string? input = null;
        string? output = null;
        string? browser = null;
        var paper = PaperSize.A4;
        var landscape = false;
        var keepHtml = false;
        var force = false;
        var collision = CollisionPolicy.Fail;
        var managed = false;
        var json = false;
        var optionsSeen = new HashSet<string>(StringComparer.Ordinal);
        var positionalOnly = false;
        var collisionSpecified = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (positionalOnly)
            {
                if (input is not null) return ParsedConvert.Failure("Only one input is supported.");
                input = argument;
                continue;
            }
            if (argument == "--")
            {
                positionalOnly = true;
                continue;
            }
            if (argument is "--help" or "-h") return ParsedConvert.HelpResult();
            if (!argument.StartsWith('-'))
            {
                if (input is not null) return ParsedConvert.Failure("Only one input is supported.");
                input = argument;
                continue;
            }

            string? value = null;
            string name = argument;
            var equals = argument.IndexOf('=');
            if (equals > 0)
            {
                name = argument[..equals];
                value = argument[(equals + 1)..];
            }
            var canonicalName = name == "-o" ? "--output" : name;
            if (!optionsSeen.Add(canonicalName))
                return ParsedConvert.Failure($"Option may be supplied only once: {name}");

            string RequiredValue()
            {
                if (value is not null) return value;
                if (++index >= args.Length)
                    throw new ArgumentException($"Option requires a value: {name}");
                return args[index];
            }

            try
            {
                switch (name)
                {
                    case "-o":
                    case "--output":
                        output = RequiredValue();
                        break;
                    case "--paper":
                        var paperValue = RequiredValue();
                        if (!Enum.TryParse<PaperSize>(paperValue, ignoreCase: true, out paper))
                            return ParsedConvert.Failure(
                                "--paper must be A4, Letter, or Legal.");
                        break;
                    case "--landscape":
                        if (value is not null)
                            return ParsedConvert.Failure("--landscape does not take a value.");
                        landscape = true;
                        break;
                    case "--keep-html":
                        if (value is not null)
                            return ParsedConvert.Failure("--keep-html does not take a value.");
                        keepHtml = true;
                        break;
                    case "--force":
                        if (value is not null)
                            return ParsedConvert.Failure("--force does not take a value.");
                        force = true;
                        break;
                    case "--collision":
                        collisionSpecified = true;
                        var collisionValue = RequiredValue();
                        collision = collisionValue.ToLowerInvariant() switch
                        {
                            "fail" => CollisionPolicy.Fail,
                            "counter" => CollisionPolicy.Counter,
                            _ => throw new ArgumentException(
                                "--collision must be fail or counter.")
                        };
                        break;
                    case "--browser":
                        browser = RequiredValue();
                        break;
                    case "--managed-browser":
                        if (value is not null)
                            return ParsedConvert.Failure("--managed-browser does not take a value.");
                        managed = true;
                        break;
                    case "--json":
                        if (value is not null)
                            return ParsedConvert.Failure("--json does not take a value.");
                        json = true;
                        break;
                    default:
                        return ParsedConvert.Failure($"Unknown convert option: {name}");
                }
            }
            catch (ArgumentException exception)
            {
                return ParsedConvert.Failure(exception.Message);
            }
        }

        if (input is null) return ParsedConvert.Failure("Missing Markdown input.");
        if (force && collisionSpecified)
            return ParsedConvert.Failure(
                "--force and --collision are mutually exclusive.");
        if (managed && browser is not null)
            return ParsedConvert.Failure(
                "--managed-browser and --browser are mutually exclusive.");

        return new ParsedConvert(
            new ConvertOptions
            {
                Input = input,
                Output = output,
                Paper = paper,
                Landscape = landscape,
                KeepHtml = keepHtml,
                Force = force,
                Collision = collision,
                BrowserPath = browser,
                ManagedBrowserOnly = managed
            },
            json,
            false,
            null);
    }

    private static async Task<bool> PrintProbeAsync(
        string browser,
        CancellationToken cancellationToken)
    {
        var root = Path.Combine(Path.GetTempPath(), "md2pdf-doctor-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        var html = Path.Combine(root, "probe.html");
        var pdf = Path.Combine(root, "probe.pdf");
        try
        {
            await File.WriteAllTextAsync(
                html,
                "<!doctype html><html><body><h1>MD2PDF probe</h1><p>local only</p>" +
                new string('x', 2000) + "</body></html>",
                cancellationToken).ConfigureAwait(false);
            var printed = await BrowserRunner.PrintAsync(
                browser, html, pdf, TimeSpan.FromSeconds(30), cancellationToken)
                .ConfigureAwait(false);
            return printed.Success && PdfValidation.IsUsable(pdf, out _);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (Exception exception) when (exception is IOException
                                                   or UnauthorizedAccessException)
            {
                // Doctor cleanup is best effort.
            }
        }
    }

    private static bool TestWritableDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, ".write-probe-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
            }
            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or ArgumentException)
        {
            return false;
        }
    }

    private static async Task WriteJsonAsync<T>(
        T result,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type,
        TextWriter writer)
    {
        var json = JsonSerializer.Serialize(result, type);
        await writer.WriteLineAsync(json).ConfigureAwait(false);
    }

    private static bool WantsJson(IEnumerable<string> args) =>
        args.Any(argument => argument.Equals("--json", StringComparison.Ordinal));

    private static async Task<int> WriteStructuredFailureAsync(
        string[] args,
        string error,
        int exitCode,
        TextWriter stdout)
    {
        var command = args.FirstOrDefault()?.ToLowerInvariant();
        switch (command)
        {
            case "doctor":
                var doctor = new DoctorResult
                {
                    Version = Version,
                    ExitCode = exitCode
                };
                doctor.Errors.Add(error);
                await WriteJsonAsync(
                    doctor,
                    Md2PdfJsonContext.Default.DoctorResult,
                    stdout).ConfigureAwait(false);
                break;
            case "browser":
                var browser = new BrowserManagementResult
                {
                    Action = args.Skip(1).FirstOrDefault(argument => argument != "--json") ?? "",
                    Version = ManagedBrowserManager.Manifest.Version,
                    ExitCode = exitCode
                };
                browser.Errors.Add(error);
                await WriteJsonAsync(
                    browser,
                    Md2PdfJsonContext.Default.BrowserManagementResult,
                    stdout).ConfigureAwait(false);
                break;
            case "explorer":
                var explorer = new ExplorerManagementResult
                {
                    Action = args.Skip(1).FirstOrDefault(argument => argument != "--json") ?? "",
                    ExitCode = exitCode
                };
                explorer.Errors.Add(error);
                await WriteJsonAsync(
                    explorer,
                    Md2PdfJsonContext.Default.ExplorerManagementResult,
                    stdout).ConfigureAwait(false);
                break;
            default:
                var conversion = new ConversionResult { ExitCode = exitCode };
                conversion.Errors.Add(error);
                await WriteJsonAsync(
                    conversion,
                    Md2PdfJsonContext.Default.ConversionResult,
                    stdout).ConfigureAwait(false);
                break;
        }
        return exitCode;
    }

    private static async Task<int> UsageErrorAsync(string error, TextWriter stderr)
    {
        await stderr.WriteLineAsync($"error: {error}").ConfigureAwait(false);
        await stderr.WriteLineAsync("Run `md2pdf --help` for usage.").ConfigureAwait(false);
        return ExitCodes.Usage;
    }

    private sealed record ParsedConvert(
        ConvertOptions? Options,
        bool Json,
        bool Help,
        string? Error)
    {
        public static ParsedConvert Failure(string error) => new(null, false, false, error);
        public static ParsedConvert HelpResult() => new(null, false, true, null);
    }

    private const string Usage = """
        md2pdf — safe, offline Markdown-to-PDF conversion

        Usage:
          md2pdf convert <input.md> [options]
          md2pdf <input.md> [options]
          md2pdf doctor [--json]
          md2pdf browser install|remove|status [--json]
          md2pdf explorer install|remove|status [--json]
          md2pdf --version

        Conversion is offline. If no Chromium-family browser is installed, run:
          md2pdf browser install
        """;

    private const string ConvertUsage = """
        Usage: md2pdf convert <input.md> [options]

          -o, --output <file>          output PDF (default: beside input)
              --paper A4|Letter|Legal  paper size (default: A4)
              --landscape             landscape orientation
              --keep-html             retain self-contained HTML
              --force                 replace existing output after success
              --collision fail|counter
                                      collision policy (default: fail)
              --browser <path>        use only this Chromium-family browser
              --managed-browser       force the MD2PDF-managed browser
              --json                  emit one JSON result
        """;
}
