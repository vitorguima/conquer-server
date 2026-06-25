// TODO-M1: NHibernate removed - using NHibernate;
// TODO-M1: NHibernate removed - using NHibernate.Criterion;
// TODO-M1: NHibernate removed - using NHibernate.SqlCommand;
using System;
using Redux.Structures;
using Redux.Database.Domain;

namespace Redux.Database.Repositories
{
    public class MagicTypeRepository : Repository<uint, DbMagicType>
    {
        public DbMagicType GetMagicTypeBySkill(ConquerSkill _skill)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
        public DbMagicType GetMagictypeByIDAndLevel(ushort _id, ushort _level)
        {
            throw new NotImplementedException("TODO-M1: NHibernate removed");
        }
    }
}
