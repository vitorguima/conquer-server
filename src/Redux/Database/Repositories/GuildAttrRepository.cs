// TODO-M1: NHibernate removed - using NHibernate.Criterion;
using Redux.Database.Domain;
using Redux.Enum;
using System;
using System.Collections.Generic;

namespace Redux.Database.Repositories
{
    public class GuildAttrRepository : Repository<uint, DbGuildAttr>
    {
        public void DeleteGuildAttr(uint userId, uint guildId, GuildRank guildRank)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
        public void DeleteAttr(uint guildId)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
        public DbGuildAttr GetGuildId(uint UID)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
        public IList<DbGuildAttr> GetMembers(uint GuildId)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
    }
}

