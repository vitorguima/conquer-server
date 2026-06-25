// TODO-M1: NHibernate removed - using NHibernate.Criterion;
// TODO-M1: NHibernate removed - using NHibernate.SqlCommand;
using System;
using System.Collections.Generic;
using Redux.Database.Domain;

namespace Redux.Database.Repositories
{
    public class SobRepository : Repository<uint, DbSob>
    {
        public DbSob GetByUID(uint uid)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
        public IList<DbSob> GetSOBByMap(uint _map)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
        public void SetGwWinner(string name)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
    }
}

