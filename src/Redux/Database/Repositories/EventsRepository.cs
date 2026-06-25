// TODO-M1: NHibernate removed - using NHibernate.Criterion;
using Redux.Database.Domain;
using Redux.Enum;
using System;
using System.Collections.Generic;

namespace Redux.Database.Repositories
{
    public class EventsRepository : Repository<uint, DbEvents>
    {
        public DbEvents GetWinner()
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }

        public IList<DbGuildAttr> GetMembers(uint GuildId)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
    }
}

