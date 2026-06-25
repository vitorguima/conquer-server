// TODO-M1: NHibernate removed - using NHibernate.Criterion;
// TODO-M1: NHibernate removed - using NHibernate.SqlCommand;
// TODO-M1: NHibernate removed - using NHibernate.Transform;
using Redux.Database.Domain;
using System;
using System.Collections.Generic;

namespace Redux.Database.Repositories
{
    public class NobilityRepository : Repository<uint, DbNobility>
    {
        public DbNobility GetByName(string name)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
        public DbNobility GetByUID(uint UID)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
        public long GetNobilityRank(long Donation)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
        public IList<DbNobility> NobilityPages()
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
    }
}

