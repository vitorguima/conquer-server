using System.Data;
using Dapper;

namespace Conquer.Database
{
    public sealed class DbAccount
    {
        public int AccountId { get; init; }
        public string Username { get; init; } = "";
        public string Password { get; init; } = "";
        public string Salt { get; init; } = "";
    }

    public sealed class AccountRepository
    {
        private readonly ConnectionFactory _factory;

        public AccountRepository(ConnectionFactory factory)
        {
            _factory = factory;
        }

        public DbAccount? FindByUsername(string username)
        {
            using var conn = _factory.Create();
            return conn.QueryFirstOrDefault<DbAccount>(
                "SELECT AccountID AS AccountId, Username, Password, Salt FROM account WHERE Username = @username LIMIT 1",
                new { username });
        }
    }
}
