// TODO-M1: NHibernate removed - using NHibernate.Criterion;
// TODO-M1: NHibernate removed - using NHibernate.SqlCommand;
using Redux.Database.Domain;
using System;

namespace Redux.Database.Repositories
{
    public class AccountRepository : Repository<uint, DbAccount>
    {
        public void ResetLoginTokens()
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
        public DbAccount GetByToken(uint token)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
        public DbAccount GetByName(string name)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
        public void DeleteCharacter(uint _uid)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
    }
}

