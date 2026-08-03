namespace Md2Pdf.Core;

public static class ProductPaths
{
    public static string ManagedBrowserRoot
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                var local = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(local, "md2pdf", "browsers");
            }

            var xdg = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            if (!string.IsNullOrWhiteSpace(xdg))
                return Path.Combine(xdg, "md2pdf", "browsers");

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "md2pdf", "browsers");
        }
    }

    public static string LogDirectory
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                var local = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(local, "md2pdf", "logs");
            }

            var state = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
            if (!string.IsNullOrWhiteSpace(state))
                return Path.Combine(state, "md2pdf", "logs");

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "state", "md2pdf", "logs");
        }
    }
}
