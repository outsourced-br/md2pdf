using System.IO.Compression;
using Md2Pdf.Core;

namespace Md2Pdf.Tests;

public sealed class ManagedBrowserSafetyTests
{
    [Fact]
    public void PinnedManifestHasExactSupportedPlatformsAndHashes()
    {
        var manifest = ManagedBrowserManager.Manifest;

        Assert.Equal("151.0.7922.71", manifest.Version);
        Assert.Equal(
            ["linux-x64", "win-x64"],
            manifest.Platforms.Select(item => item.Rid).Order());
        Assert.All(manifest.Platforms, platform =>
        {
            Assert.StartsWith(
                "https://storage.googleapis.com/chrome-for-testing-public/" +
                manifest.Version + "/",
                platform.Url,
                StringComparison.Ordinal);
            Assert.True(ManagedBrowserManager.IsSha256(platform.Sha256));
            Assert.Contains("chrome-headless-shell", platform.Executable, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void RejectsZipSlipWithoutWritingOutsideDestination()
    {
        using var temp = new TemporaryDirectory();
        var archivePath = temp.File("malicious.zip");
        var destination = temp.File("payload");
        var escaped = temp.File("escaped.txt");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../escaped.txt");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("must not escape");
        }

        Assert.Throws<InvalidDataException>(() =>
            ManagedBrowserManager.ExtractSafely(archivePath, destination));
        Assert.False(File.Exists(escaped));
    }

    [Fact]
    public void RejectsSymbolicLinkEntries()
    {
        using var temp = new TemporaryDirectory();
        var archivePath = temp.File("symlink.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("link");
            entry.ExternalAttributes = (0xA000 | 0x1FF) << 16;
            using var writer = new StreamWriter(entry.Open());
            writer.Write("target");
        }

        Assert.Throws<InvalidDataException>(() =>
            ManagedBrowserManager.ExtractSafely(archivePath, temp.File("payload")));
    }
}
