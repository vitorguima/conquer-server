// TODO-M1: NHibernate removed - using NHibernate.Criterion;
// TODO-M1: NHibernate removed - using NHibernate.SqlCommand;
using System;
using System.Collections.Generic;
using Redux.Database.Domain;

namespace Redux.Database.Repositories
{
    public class AssociateRepository : Repository<uint, DbAssociate>
    {
        public IList<DbAssociate> GetUserAssociates(uint id)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }

        public void Remove(uint ownerid, uint associateid, Enum.AssociateType type)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }

        public bool AssociateExists(uint ownerid, uint associateid, Enum.AssociateType type)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
    }
}

