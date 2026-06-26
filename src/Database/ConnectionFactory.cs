using System;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace Conquer.Database
{
    public sealed class ConnectionFactory
    {
        private readonly string _connectionString;

        public ConnectionFactory(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");
        }

        public MySqlConnection Create()
        {
            var conn = new MySqlConnection(_connectionString);
            conn.Open();
            return conn;
        }
    }
}
