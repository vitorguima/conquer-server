using System.Text;

namespace ClientPatcher;

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

        // 2. Validate; errors → exit 2 (nothing written). Warnings are printed but
        //    do not block the run (design §2, §8).
        var validation = InputValidator.Validate(options);
        if (!validation.Ok)
        {
            foreach (var error in validation.Errors)
            {
                Console.Error.WriteLine("error: " + error);
            }

            return (int)ExitCode.Validation;
        }

        foreach (var warning in validation.Warnings)
        {
            Console.Error.WriteLine("WARNING: " + warning);
        }

        // 3. Resolve target files under --client. None → exit 2.
        var targets = TargetResolver.Resolve(options);
        if (targets.Count == 0)
        {
            Console.Error.WriteLine("error: no Conquer.exe or server.dat found in --client dir");
            return (int)ExitCode.Validation;
        }

        return RunPatch(options, targets, validation.Warnings);
    }

    /// <summary>
    /// Patch loop (design §8, Data Flow): per resolved target read the source into
    /// an in-memory copy (source never opened for write), build the replacement
    /// payload, run the engine, then — if any file matched — write backups and the
    /// length-preserved patched bytes, and emit the report. Exit-code mapping per §8.
    /// </summary>
    private static int RunPatch(
        PatchOptions options,
        IReadOnlyList<TargetFile> targets,
        IReadOnlyList<string> warnings)
    {
        var endpoint = EndpointBuilder.Build(options);
        var findBytes = Encoding.ASCII.GetBytes(options.Find!);

        // First pass: read + apply for every target. No disk writes here so a
        // length-guard rejection or not-found short-circuits before any output
        // lands (design §8: non-zero paths write nothing for the failing condition).
        var pending = new List<(TargetFile target, byte[] sourceBytes, byte[] outputBytes, PatchResult result)>();
        foreach (var target in targets)
        {
            byte[] sourceBytes;
            try
            {
                // Source is opened READ-ONLY into an in-memory copy and never
                // written (NFR-2, AC-2.1).
                sourceBytes = File.ReadAllBytes(target.SourcePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"error: could not read {target.SourcePath}: {ex.Message}");
                return (int)ExitCode.IoError;
            }

            var (output, result, error) = PatchEngine.Apply(sourceBytes, findBytes, endpoint.HostBytes);
            result.FileName = target.Name;

            if (error == PatchError.NewLongerThanOld)
            {
                Console.Error.WriteLine(
                    $"error: new host '{Encoding.ASCII.GetString(endpoint.HostBytes)}' " +
                    $"({endpoint.HostBytes.Length} bytes) longer than --find slot ({findBytes.Length} bytes)");
                return (int)ExitCode.Validation;
            }

            if (error == PatchError.FindNotFound)
            {
                // Skip this file; not-found is only fatal when ALL targets miss.
                continue;
            }

            pending.Add((target, sourceBytes, output, result));
        }

        // No target file matched --find → exit 3, nothing written (design §8).
        if (pending.Count == 0)
        {
            Console.Error.WriteLine("error: search string not found in Conquer.exe or server.dat");
            return (int)ExitCode.NotFound;
        }

        // Second pass: write <out>.bak then patched bytes (length-preserved).
        var results = new List<PatchResult>(pending.Count);
        foreach (var (target, sourceBytes, outputBytes, result) in pending)
        {
            try
            {
                // Backup the original-copy bytes BEFORE the patched bytes land.
                BackupWriter.WriteBackup(target.OutputPath, sourceBytes);

                var dir = Path.GetDirectoryName(target.OutputPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllBytes(target.OutputPath, outputBytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"error: could not write {target.OutputPath}: {ex.Message}");
                return (int)ExitCode.IoError;
            }

            results.Add(result);
        }

        // Report to stdout, exit 0.
        ReportWriter.Write(Console.Out, results, warnings, endpoint);
        return (int)ExitCode.Ok;
    }
}
