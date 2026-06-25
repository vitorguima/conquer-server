// TODO-M1: NHibernate removed - using NHibernate.Criterion;
// TODO-M1: NHibernate removed - using NHibernate.SqlCommand;
// TODO-M1: NHibernate removed - using NHibernate.Transform;
using Redux.Database.Domain;
using System;
using System.Collections.Generic;

namespace Redux.Database.Repositories
{
    public class RebornRepository : Repository<uint, DbRebornPath>
    {
        public IList<DbRebornPath> GetRebornByPath(uint _path)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
    }
}

