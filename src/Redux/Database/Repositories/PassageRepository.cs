// TODO-M1: NHibernate removed - using NHibernate.Criterion;
// TODO-M1: NHibernate removed - using NHibernate.SqlCommand;
using Redux.Database.Domain;
using Redux.Game_Server;
using System;

namespace Redux.Database.Repositories
{
    public class PassageRepository : Repository<uint, DbPassage>
    {
        public DbPortal GetPortalByMapAndID(ushort _map, uint _id)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
    }
}

