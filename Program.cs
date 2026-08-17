using System.CommandLine;

namespace Shaun;

internal sealed class Program
{
    public static async Task<int> Main(string[] args)
    {
        Argument<string[]> urlsArg = new("urls")
        {
            Description = "URLs to download from",
            Arity = ArgumentArity.OneOrMore,
        };
        Option<string?> alsoOpt = new("--also", ["-a"])
        {
            Description =
                "Additional authors (comma-delimited Bluesky handles/DIDs or Twitter handles; "
                + "bang-delimited per-attachment)",
        };
        Option<bool> compactOpt = new("--compact", ["-c"])
        {
            Description = "Omit DIDs from Bluesky filenames",
        };
        Option<bool> parallelOpt = new("--parallel", ["-p"])
        {
            Description = "Download media in parallel",
        };
        Option<bool> mtimeOpt = new("--mtime", ["-m"])
        {
            Description =
                "Set each file's modification time to the post's reported publishing time",
        };
        Option<string?> outputOpt = new("--output", ["-o"])
        {
            Description = "Output directory (defaults to the current directory)",
        };

        RootCommand root = new("Downloads media from Bluesky and Twitter/X posts.")
        {
            Arguments = { urlsArg },
            Options = { alsoOpt, compactOpt, parallelOpt, mtimeOpt, outputOpt },
        };

        root.SetAction(
            async (result, ct) =>
            {
                var options = new DownloadOptions(
                    result.GetValue(urlsArg) ?? [],
                    result.GetValue(alsoOpt),
                    result.GetValue(compactOpt),
                    result.GetValue(parallelOpt),
                    result.GetValue(mtimeOpt),
                    result.GetValue(outputOpt) ?? Environment.CurrentDirectory
                );
                return await RunAsync(options, ct);
            }
        );

        return await root.Parse(args).InvokeAsync();
    }

    private static async Task<int> RunAsync(DownloadOptions options, CancellationToken ct)
    {
        var progress = new Progress<string>(Console.WriteLine);
        RunResult result = await Backends.RunAsync(options, progress, ct);
        foreach (string error in result.Errors)
            Console.Error.WriteLine(error);
        return result.Failed > 0 ? 1 : 0;
    }
}
