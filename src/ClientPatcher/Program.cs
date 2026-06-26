namespace ClientPatcher;

/// <summary>
/// Thin CLI shell (design §8): parse args, handle <c>--help</c>, then delegate the
/// full pipeline to <see cref="Patcher.Run"/>. All exit-code mapping lives in
/// <see cref="Patcher"/> so behaviour is identical whether invoked here or in tests.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        // 1. Parse args → PatchOptions (defaults applied). Malformed/unknown flag
        //    → ArgParseException → exit 2 (design §8).
        PatchOptions options;
        try
        {
            options = ArgumentParser.Parse(args);
        }
        catch (ArgParseException ex)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(ArgumentParser.UsageText());
            return (int)ExitCode.Validation;
        }

        // --help/-h → print usage, exit 0.
        if (options.ShowHelp)
        {
            Console.WriteLine(ArgumentParser.UsageText());
            return (int)ExitCode.Ok;
        }

        // 2. Delegate validate → resolve → patch → report to the runner.
        return Patcher.Run(options, Console.Out, Console.Error);
    }
}
