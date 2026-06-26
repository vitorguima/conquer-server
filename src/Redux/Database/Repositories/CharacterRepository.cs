// TODO-M1: NHibernate removed - using NHibernate.Criterion;
using Redux.Database.Domain;
using System;

namespace Redux.Database.Repositories
{
    public class CharacterRepository : Repository<uint, DbCharacter>
    {
        public void ResetOnlineCharacters()
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
        public DbCharacter GetByUID(uint uid)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
        public DbCharacter GetByName(string name)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
        public void CreateEntry(DbCharacter character)
        {
            Add(character);
        }
    }
}

