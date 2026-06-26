namespace Conquer.Crypto
{
    /// <summary>
    /// Minimal cipher-agnostic read/write surface shared by the auth-path
    /// <see cref="TQCipher"/> and the game-path <c>GameCipher</c>. Only the
    /// methods both ciphers genuinely share live here; direction/key-swap
    /// operations (TQCipher.GenerateKeys, GameCipher.SetKey/SetIvs) stay off
    /// the interface so adding <c>: ICipher</c> is a pure no-op for auth.
    /// </summary>
    public interface ICipher
    {
        void Encrypt(byte[] data, int offset, int length);
        void Decrypt(byte[] data, int offset, int length);
    }
}
