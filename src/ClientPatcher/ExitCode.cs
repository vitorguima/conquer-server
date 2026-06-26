namespace ClientPatcher;

/// <summary>
/// Process exit codes (design §8). Mapped from pipeline outcomes by <see cref="Program"/>.
/// </summary>
public enum ExitCode
{
    Ok = 0,
    Validation = 2,
    NotFound = 3,
    IoError = 4,
}
