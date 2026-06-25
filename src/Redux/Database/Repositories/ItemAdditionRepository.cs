// TODO-M1: NHibernate removed - using NHibernate.Criterion;
// TODO-M1: NHibernate removed - using NHibernate.SqlCommand;
using Redux.Database.Domain;
using System;

namespace Redux.Database.Repositories
{
    public class ItemAdditionRepository : Repository<uint, DbItemAddition>
    {
        public DbItemAddition GetByItem(Structures.ConquerItem item)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
    }
}

