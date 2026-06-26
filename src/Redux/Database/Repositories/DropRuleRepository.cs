// TODO-M1: NHibernate removed - using NHibernate.Criterion;
// TODO-M1: NHibernate removed - using NHibernate.SqlCommand;
using System;
using System.Collections.Generic;
using Redux.Database.Domain;

namespace Redux.Database.Repositories
{
    public class DropRuleRepository : Repository<uint, DbDropRule>
    {
        public IList<DbDropRule> GetRulesByMonsterType(uint _monsterType)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
    }
}

