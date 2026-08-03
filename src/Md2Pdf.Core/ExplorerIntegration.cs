using Microsoft.Win32;
using System.Runtime.Versioning;

namespace Md2Pdf.Core;

public static class ExplorerIntegration
{
    private const string VerbKey =
        @"Software\Classes\SystemFileAssociations\.md\shell\md2pdf";

    public static ExplorerManagementResult Install(string executableDirectory)
    {
        if (!OperatingSystem.IsWindows()) return Unsupported("install");
        return InstallWindows(executableDirectory);
    }

    public static ExplorerManagementResult Remove()
    {
        if (!OperatingSystem.IsWindows()) return Unsupported("remove");
        return RemoveWindows();
    }

    public static ExplorerManagementResult Status()
    {
        if (!OperatingSystem.IsWindows()) return Unsupported("status");
        return StatusWindows();
    }

    [SupportedOSPlatform("windows")]
    private static ExplorerManagementResult InstallWindows(string executableDirectory)
    {
        var result = new ExplorerManagementResult
        {
            Action = "install",
            ExitCode = ExitCodes.InstallationFailure
        };
        var helper = Path.GetFullPath(
            Path.Combine(executableDirectory, "md2pdf-explorer.exe"));
        if (!File.Exists(helper))
        {
            result.Errors.Add($"Explorer helper not found beside the CLI: {helper}");
            return result;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(VerbKey, writable: true);
            key.SetValue("MUIVerb", "Convert Markdown to PDF", RegistryValueKind.String);
            key.SetValue("Icon", $"\"{helper}\"", RegistryValueKind.String);
            key.SetValue("MultiSelectModel", "Single", RegistryValueKind.String);
            using var command = key.CreateSubKey("command", writable: true);
            var value = $"\"{helper}\" \"%1\"";
            command.SetValue(null, value, RegistryValueKind.String);
            result.Success = true;
            result.Installed = true;
            result.Command = value;
            result.ExitCode = ExitCodes.Success;
            return result;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
                                               or IOException
                                               or System.Security.SecurityException)
        {
            result.Errors.Add(exception.Message);
            return result;
        }
    }

    [SupportedOSPlatform("windows")]
    private static ExplorerManagementResult RemoveWindows()
    {
        var result = new ExplorerManagementResult
        {
            Action = "remove",
            ExitCode = ExitCodes.InstallationFailure
        };
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(VerbKey, throwOnMissingSubKey: false);
            result.Success = true;
            result.Installed = false;
            result.ExitCode = ExitCodes.Success;
            return result;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
                                               or IOException
                                               or System.Security.SecurityException)
        {
            result.Errors.Add(exception.Message);
            return result;
        }
    }

    [SupportedOSPlatform("windows")]
    private static ExplorerManagementResult StatusWindows()
    {
        var result = new ExplorerManagementResult
        {
            Action = "status",
            ExitCode = ExitCodes.Success,
            Success = true
        };
        try
        {
            using var command = Registry.CurrentUser.OpenSubKey(VerbKey + @"\command");
            result.Command = command?.GetValue(null) as string;
            result.Installed = !string.IsNullOrWhiteSpace(result.Command);
            return result;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
                                               or IOException
                                               or System.Security.SecurityException)
        {
            result.Success = false;
            result.ExitCode = ExitCodes.InstallationFailure;
            result.Errors.Add(exception.Message);
            return result;
        }
    }

    private static ExplorerManagementResult Unsupported(string action)
    {
        var result = new ExplorerManagementResult
        {
            Action = action,
            Installed = false,
            ExitCode = ExitCodes.InstallationFailure
        };
        result.Errors.Add("Windows Explorer integration is available only on Windows.");
        return result;
    }
}
