// TODO-M1: NHibernate removed - using NHibernate.Criterion;
// TODO-M1: NHibernate removed - using NHibernate.SqlCommand;
using Redux.Database.Domain;
using System;

namespace Redux.Database.Repositories
{
    public class PortalRepository : Repository<uint, DbPortal>
    {
        public DbPortal GetPortalByPassage(DbPassage _passage)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
    }
}

