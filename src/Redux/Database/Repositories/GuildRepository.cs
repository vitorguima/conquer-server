// TODO-M1: NHibernate removed - using NHibernate.Criterion;
// TODO-M1: NHibernate removed - using NHibernate.Transform;
using Redux.Database.Domain;
using System;
using System.Collections.Generic;

namespace Redux.Database.Repositories
{
    public class GuildRepository : Repository<uint, DbGuild>
    {
        public IList<DbGuildMemberInfo> GetMemberInfo(int guildId)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
        public DbGuild GetGuildByName(string name)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
        public IList<DbGuild> GetAll()
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
    }
}

