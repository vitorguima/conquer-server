// TODO-M1: NHibernate removed - using NHibernate.Criterion;
// TODO-M1: NHibernate removed - using NHibernate.SqlCommand;
using System;
using System.Collections.Generic;
using Redux.Database.Domain;

namespace Redux.Database.Repositories
{
    public class SkillRepository : Repository<uint, DbSkill>
    {
        public IList<DbSkill> GetUserSkills(uint uid)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
        public bool SkillExists(uint owner, ushort id)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
    }
}
