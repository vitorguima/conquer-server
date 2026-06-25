// TODO-M1: NHibernate removed - using NHibernate.Criterion;
// TODO-M1: NHibernate removed - using NHibernate.SqlCommand;
using System;
using System.Collections.Generic;
using Redux.Database.Domain;

namespace Redux.Database.Repositories
{
    public class MineDropRepository : Repository<uint, DbMineDrop>
    {
        public IList<DbMineDrop> GetDropByMap(ushort _map)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
    }
}

