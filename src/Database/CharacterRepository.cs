using System.Data;
using Dapper;

namespace Conquer.Database
{
    public sealed class DbCharacter
    {
        public int CharacterID { get; init; }
        public int AccountID { get; init; }
        public string Name { get; init; } = "";
        public int Mesh { get; init; }
        public int Avatar { get; init; }
        public int Level { get; init; } = 1;
        public int MapID { get; init; } = 1010;
        public int X { get; init; } = 61;
        public int Y { get; init; } = 109;
        public int Silver { get; init; } = 1000;
        public int Strength { get; init; }
        public int Agility { get; init; }
        public int Vitality { get; init; }
        public int Spirit { get; init; }
        public int HealthPoints { get; init; }
        public int ManaPoints { get; init; }
    }

    public sealed class CharacterRepository
    {
        private readonly ConnectionFactory _factory;

        public CharacterRepository(ConnectionFactory factory)
        {
            _factory = factory;
        }

        public DbCharacter? FindByAccountId(int accountId)
        {
            using var conn = _factory.Create();
            return conn.QueryFirstOrDefault<DbCharacter>(
                @"SELECT CharacterID, AccountID, Name, Mesh, Avatar, Level, MapID, X, Y,
                         Silver, Strength, Agility, Vitality, Spirit, HealthPoints, ManaPoints
                  FROM characters WHERE AccountID = @accountId LIMIT 1",
                new { accountId });
        }

        public void Insert(DbCharacter character)
        {
            using var conn = _factory.Create();
            conn.Execute(
                @"INSERT INTO characters (AccountID, Name, Mesh, Avatar, Level, MapID, X, Y,
                                          Silver, Strength, Agility, Vitality, Spirit, HealthPoints, ManaPoints)
                  VALUES (@AccountID, @Name, @Mesh, @Avatar, @Level, @MapID, @X, @Y,
                          @Silver, @Strength, @Agility, @Vitality, @Spirit, @HealthPoints, @ManaPoints)",
                character);
        }
    }
}
