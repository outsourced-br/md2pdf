namespace Md2Pdf.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args) =>
        await CliApplication.RunAsync(
            args, Console.Out, Console.Error, CancellationToken.None).ConfigureAwait(false);
}
