// TODO-M1: NHibernate removed - using NHibernate.Criterion;
// TODO-M1: NHibernate removed - using NHibernate.SqlCommand;
using Redux.Database.Domain;
using System;

namespace Redux.Database.Repositories
{
    public class StatRepository : Repository<uint, DbStat>
    {
        public DbStat GetByProfessionAndLevel(ushort _professionType, byte _level)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
    }
}

