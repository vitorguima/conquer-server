// TODO-M1: NHibernate removed - using NHibernate.Criterion;
// TODO-M1: NHibernate removed - using NHibernate.SqlCommand;
using System;
using System.Collections.Generic;
using Redux.Database.Domain;

namespace Redux.Database.Repositories
{
    public class ProficiencyRepository : Repository<uint, DbProficiency>
    {
        public IList<DbProficiency> GetUserProficiency(uint owner)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
        public bool ProficiencyExists(uint owner, ushort id)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
    }
}
