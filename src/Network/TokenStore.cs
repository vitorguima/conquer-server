using System.Collections.Concurrent;

namespace Conquer.Network
{
    public static class TokenStore
    {
        private static readonly ConcurrentDictionary<ulong, int> _tokens = new();

        public static void Add(ulong token, int accountId)
            => _tokens[token] = accountId;

        public static bool TryConsume(ulong token, out int accountId)
            => _tokens.TryRemove(token, out accountId);
    }
}
