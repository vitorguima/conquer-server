using System.Text;

namespace ClientPatcher;

internal static class Program
{
    /// <summary>
    /// Centralized user-facing error strings, grouped by the <see cref="ExitCode"/>
    /// they map to (design §8 exit-code table / Error Handling table). Keeping the
    /// wording here makes the message→exit-code mapping explicit and auditable.
    /// </summary>
    private static class Errors
    {
        // → ExitCode.Validation (2)
        public const string NoTargets = "no Conquer.exe or server.dat found in --client dir";

        public static string NewLongerThanOld(string host, int newLen, int slotLen) =>
            $"new host '{host}' ({newLen} bytes) longer than --find slot ({slotLen} bytes)";

        // → ExitCode.NotFound (3)
        public const string FindNotFound = "search string not found in Conquer.exe or server.dat";

        // → ExitCode.IoError (4)
        public static string CouldNotRead(string path, string reason) =>
            $"could not read {path}: {reason}";

        public static string CouldNotWrite(string path, string reason) =>
            $"could not write {path}: {reason}";
    }

    /// <summary>Write a single "error: ..." line to stderr.</summary>
    private static void WriteError(string message) =>
        Console.Error.WriteLine("error: " + message);

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
            WriteError(ex.Message);
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
                WriteError(error);
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
            WriteError(Errors.NoTargets);
            return (int)ExitCode.Validation;
        }

        return RunPatch(options, targets);
    }

    /// <summary>
    /// Patch loop (design §8, Data Flow): per resolved target read the source into
    /// an in-memory copy (source never opened for write), build the replacement
    /// payload, run the engine, then — if any file matched — write backups and the
    /// length-preserved patched bytes, and emit the report. Exit-code mapping per §8.
    /// </summary>
    private static int RunPatch(
        PatchOptions options,
        IReadOnlyList<TargetFile> targets)
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
                WriteError(Errors.CouldNotRead(target.SourcePath, ex.Message));
                return (int)ExitCode.IoError;
            }

            var (output, result, error) = PatchEngine.Apply(sourceBytes, findBytes, endpoint.HostBytes);
            result.FileName = target.Name;

            if (error == PatchError.NewLongerThanOld)
            {
                WriteError(Errors.NewLongerThanOld(
                    Encoding.ASCII.GetString(endpoint.HostBytes),
                    endpoint.HostBytes.Length,
                    findBytes.Length));
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
            WriteError(Errors.FindNotFound);
            return (int)ExitCode.NotFound;
        }

        // Second pass: write <out>.bak then patched bytes (length-preserved).
        var results = new List<PatchResult>(pending.Count);
        foreach (var (target, sourceBytes, outputBytes, result) in pending)
        {
            // Backup the original-copy bytes BEFORE the patched bytes land. A
            // backup-write failure aborts this file before any patched output —
            // the original is never at risk (source is never opened for write).
            try
            {
                BackupWriter.WriteBackup(target.OutputPath, sourceBytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                WriteError(Errors.CouldNotWrite(target.OutputPath + ".bak", ex.Message));
                return (int)ExitCode.IoError;
            }

            // Write patched bytes to a sibling temp file first, then move it into
            // place. A failure mid-write leaves only the temp file (cleaned up) —
            // the patched output path never holds a truncated/corrupt partial.
            try
            {
                WritePatchedAtomic(target.OutputPath, outputBytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                WriteError(Errors.CouldNotWrite(target.OutputPath, ex.Message));
                return (int)ExitCode.IoError;
            }

            results.Add(result);
        }

        // Report to stdout, exit 0.
        ReportWriter.Write(Console.Out, results, endpoint);
        return (int)ExitCode.Ok;
    }

    /// <summary>
    /// Write <paramref name="bytes"/> to <paramref name="outputPath"/> via a sibling
    /// temp file then a move into place, so a write failure can never leave a
    /// truncated/corrupt partial at the target path. The temp file is removed on
    /// failure; on success it replaces any existing patched output.
    /// </summary>
    private static void WritePatchedAtomic(string outputPath, byte[] bytes)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = outputPath + ".tmp";
        try
        {
            File.WriteAllBytes(tempPath, bytes);
            // Move overwriting any prior patched output (reuse + overwrite, design
            // §Edge Cases: output dir exists).
            File.Move(tempPath, outputPath, overwrite: true);
        }
        catch
        {
            // Best-effort cleanup of the partial temp file; never mask the original
            // IO error that aborted the write.
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // ignore cleanup failure
            }

            throw;
        }
    }
}
